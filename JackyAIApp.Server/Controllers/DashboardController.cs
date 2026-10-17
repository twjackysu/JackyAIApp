using System.Text.Json;
using System.Text.Json.Serialization;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using static JackyAIApp.Server.Common.DataAnalysisMapping;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// User-owned dashboards built from DataAnalysis summarize results. Each widget stores a
    /// re-runnable summarize plan; the data is re-fetched on view via the dashboard-scoped
    /// widget-data endpoint, which authorizes by the dashboard's visibility/shares and resolves
    /// the engine as the dashboard owner (so shared/custom-source widgets work for viewers).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController(
        ILogger<DashboardController> logger,
        IMyResponseFactory responseFactory,
        AzureSQLDBContext dbContext,
        DataAnalysisEngine defaultEngine,
        IDataSourceConfigService configService,
        IMemoryCache cache) : ApiControllerBase
    {
        private readonly ILogger<DashboardController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly DataAnalysisEngine _defaultEngine = defaultEngine;
        private readonly IDataSourceConfigService _configService = configService;
        private readonly IMemoryCache _cache = cache;

        // Built-in default source (matches DataAnalysisController).
        private const string DefaultMsSqlKey = "mssql";
        private const string DefaultTableName = "dbo.Orders";

        // "Anyone" = fully public (no login required); "Public" = any logged-in user.
        private static readonly string[] ValidVisibilities = ["Private", "Public", "Restricted", "Anyone"];

        private static readonly JsonSerializerOptions PlanJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Whether a (possibly anonymous) user may view this dashboard.</summary>
        private static bool CanView(Dashboard d, string? userId) =>
            d.Visibility == "Anyone" ||
            (userId != null && d.UserId == userId) ||
            (userId != null && d.Visibility == "Public") ||
            (userId != null && d.Visibility == "Restricted" && d.Shares.Any(s => s.UserId == userId));

        /// <summary>List dashboards the user owns or has been granted access to (restricted shares).</summary>
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var items = await _dbContext.Dashboards
                .Where(d =>
                    d.UserId == userId ||
                    (d.Visibility == "Restricted" && d.Shares.Any(s => s.UserId == userId)))
                .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                .Select(d => new DashboardSummaryDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    WidgetCount = d.Widgets.Count,
                    Visibility = d.Visibility,
                    IsOwner = d.UserId == userId,
                    CreatedAt = d.CreatedAt,
                })
                .ToListAsync(ct);
            return _responseFactory.CreateOKResponse(items);
        }

        /// <summary>Create a new (empty) dashboard.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDashboardRequest dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Name is required.");

            var entity = new Dashboard
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.Dashboards.Add(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Created dashboard {id} for user {uid}", entity.Id, userId);

            return _responseFactory.CreateOKResponse(new DashboardSummaryDto
            {
                Id = entity.Id,
                Name = entity.Name,
                WidgetCount = 0,
                Visibility = entity.Visibility,
                IsOwner = true,
                CreatedAt = entity.CreatedAt,
            });
        }

        /// <summary>Get a dashboard and its widgets (owner, or a viewer allowed by visibility — incl. anonymous for "Anyone").</summary>
        [AllowAnonymous]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId; // may be null for anonymous (allowed only for "Anyone")

            var dash = await _dbContext.Dashboards
                .Include(d => d.Widgets)
                .Include(d => d.Shares)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            // Use NotFound (not Forbidden) so a private dashboard's existence isn't leaked.
            if (dash is null || !CanView(dash, userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            var isOwner = userId != null && dash.UserId == userId;

            return _responseFactory.CreateOKResponse(new DashboardDetailDto
            {
                Id = dash.Id,
                Name = dash.Name,
                Visibility = dash.Visibility,
                IsOwner = isOwner,
                Widgets = dash.Widgets
                    .OrderBy(w => w.Y).ThenBy(w => w.X)
                    .Select(ToWidgetDto)
                    .ToList(),
            });
        }

        /// <summary>Get sharing settings for a dashboard (owner only).</summary>
        [HttpGet("{id:guid}/share")]
        public async Task<IActionResult> GetShare(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var dash = await _dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            return _responseFactory.CreateOKResponse(ToShareDto(dash));
        }

        /// <summary>Set visibility and, for Restricted, the full set of allowed users by email (owner only).</summary>
        [HttpPut("{id:guid}/share")]
        public async Task<IActionResult> UpdateShare(Guid id, [FromBody] UpdateShareRequest dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            if (!ValidVisibilities.Contains(dto.Visibility))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Invalid visibility.");

            var dash = await _dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            dash.Visibility = dto.Visibility;

            if (dto.Visibility == "Restricted")
            {
                var emails = (dto.Emails ?? [])
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0)
                    .ToList();
                var users = emails.Count == 0
                    ? []
                    : await _dbContext.Users
                        .Where(u => u.Email != null && emails.Contains(u.Email))
                        .ToListAsync(ct);
                // Owner always has access — never store a share for them.
                var desiredIds = users.Select(u => u.Id).Where(uid => uid != userId).ToHashSet();

                _dbContext.DashboardShares.RemoveRange(
                    dash.Shares.Where(s => !desiredIds.Contains(s.UserId)));
                var existingIds = dash.Shares.Select(s => s.UserId).ToHashSet();
                foreach (var uid in desiredIds.Where(uid => !existingIds.Contains(uid)))
                {
                    _dbContext.DashboardShares.Add(new DashboardShare
                    {
                        Id = Guid.NewGuid(),
                        DashboardId = dash.Id,
                        UserId = uid,
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
            else
            {
                // Private / Public — shares are irrelevant; clear them.
                _dbContext.DashboardShares.RemoveRange(dash.Shares);
            }

            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            // Re-load to return the resolved members.
            var fresh = await _dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .FirstAsync(d => d.Id == id, ct);
            return _responseFactory.CreateOKResponse(ToShareDto(fresh));
        }

        private static ShareSettingsDto ToShareDto(Dashboard dash) => new()
        {
            Visibility = dash.Visibility,
            Members = dash.Shares
                .Select(s => new ShareMemberDto { UserId = s.UserId, Email = s.User?.Email, Name = s.User?.Name })
                .ToList(),
        };

        /// <summary>
        /// Re-run a widget's saved plan and return its rows. Access is gated by the dashboard's
        /// visibility/shares (anonymous allowed only for "Anyone"); the engine is resolved as the
        /// dashboard OWNER, so custom-source widgets work for any allowed viewer.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id:guid}/widgets/{widgetId:guid}/data")]
        public async Task<IActionResult> WidgetData(Guid id, Guid widgetId, CancellationToken ct = default)
        {
            var userId = CurrentUserId; // may be null (allowed only for "Anyone")

            var dash = await _dbContext.Dashboards
                .Include(d => d.Shares)
                .Include(d => d.Widgets)
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null || !CanView(dash, userId))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            var widget = dash.Widgets.FirstOrDefault(w => w.Id == widgetId);
            if (widget is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Widget not found.");

            SummarizeRequestDto? plan;
            try
            {
                plan = JsonSerializer.Deserialize<SummarizeRequestDto>(widget.PlanJson, PlanJsonOptions);
            }
            catch
            {
                plan = null;
            }
            if (plan is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Invalid widget plan.");

            // Resolve the engine as the dashboard owner (not the viewer).
            DataAnalysisEngine engine;
            string sourceKey;
            string tableName;
            var ownsEngine = false;
            if (plan.Context?.SourceId is null)
            {
                engine = _defaultEngine;
                sourceKey = DefaultMsSqlKey;
                tableName = DefaultTableName;
            }
            else
            {
                var source = await _dbContext.DataSourceConfigs
                    .FirstOrDefaultAsync(s => s.Id == plan.Context.SourceId.Value && s.UserId == dash.UserId, ct);
                if (source is null)
                    return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Data source not found.");
                try
                {
                    engine = _configService.BuildEngine(source);
                }
                catch (DataSourceConfigException ex)
                {
                    _logger.LogWarning("WidgetData: refused saved source {sid}: {err}", source.Id, ex.Message);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, ex.Message);
                }
                sourceKey = _configService.SourceKey(source.ConnectorType);
                tableName = plan.Context.TableName ?? source.DefaultTableName ?? "";
                ownsEngine = true;
            }

            try
            {
                // The saved plan is the owner's input replayed for any viewer (anonymous for "Anyone"), so it
                // passes the same schema gate as a live DataAnalysis query.
                var fields = await DataAnalysisFieldGuard.LoadFieldNamesAsync(
                    engine, sourceKey, tableName, ownsEngine ? null : _cache, ct);
                if (fields is null)
                    return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Schema query failed.");
                if (DataAnalysisFieldGuard.Check(fields, plan) is { } planError)
                {
                    _logger.LogWarning("WidgetData: rejected widget {wid} plan: {err}", widgetId, planError);
                    return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, planError);
                }

                var result = await engine.SummarizeAsync(new SummarizeRequest
                {
                    SourceKey = sourceKey,
                    TableName = tableName,
                    ViewBy = plan.ViewBy.Select(MapGroupField).ToList(),
                    Measures = plan.Measures
                        .Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias)))
                        .ToList(),
                    Filters = MapFilters(plan.Filters),
                }, ct);

                if (result.IsSuccess)
                    return _responseFactory.CreateOKResponse(new { rows = result.Value!.Rows.Select(r => r.Data) });

                // The driver's text names servers, tables and SQL. Only the source's owner looking at their
                // own dashboard gets it, to debug their database; any other viewer — anonymous ones on an
                // "Anyone" dashboard included, and everyone for the default source (the app's own
                // database) — gets the generic message, and the detail stays in the log.
                var showDetail = ownsEngine && userId == dash.UserId && !string.IsNullOrWhiteSpace(result.ErrorMessage);
                _logger.Log(ownsEngine ? LogLevel.Warning : LogLevel.Error,
                    "WidgetData: widget {wid} query failed on table={t}: {err}", widgetId, tableName, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.InternalServerError, showDetail ? result.ErrorMessage! : "Query failed.");
            }
            finally
            {
                if (ownsEngine) engine.Dispose();
            }
        }

        /// <summary>Add a widget to a dashboard (owner only). Auto-placed below existing widgets.</summary>
        [HttpPost("{id:guid}/widgets")]
        public async Task<IActionResult> AddWidget(Guid id, [FromBody] AddWidgetRequest dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            if (string.IsNullOrWhiteSpace(dto.VizType))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "VizType is required.");

            var dash = await _dbContext.Dashboards
                .Include(d => d.Widgets)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            var w = Math.Clamp(dto.W <= 0 ? 6 : dto.W, 1, 12);
            var h = dto.H <= 0 ? 8 : dto.H;
            var nextY = dash.Widgets.Count == 0 ? 0 : dash.Widgets.Max(x => x.Y + x.H);

            var widget = new DashboardWidget
            {
                Id = Guid.NewGuid(),
                DashboardId = dash.Id,
                Title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title!.Trim(),
                VizType = dto.VizType,
                PlanJson = dto.Plan.GetRawText(),
                X = 0,
                Y = nextY,
                W = w,
                H = h,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.DashboardWidgets.Add(widget);
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);

            return _responseFactory.CreateOKResponse(ToWidgetDto(widget));
        }

        /// <summary>Rename a dashboard (owner only).</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Rename(Guid id, [FromBody] RenameDashboardRequest dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Name is required.");

            var dash = await _dbContext.Dashboards
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            dash.Name = name;
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse();
        }

        /// <summary>Delete a dashboard and its widgets (owner only).</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var dash = await _dbContext.Dashboards
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            _dbContext.Dashboards.Remove(dash); // widgets cascade
            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse();
        }

        /// <summary>Persist the grid positions/sizes of a dashboard's widgets (owner only).</summary>
        [HttpPut("{id:guid}/layout")]
        public async Task<IActionResult> UpdateLayout(Guid id, [FromBody] UpdateLayoutRequest dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var dash = await _dbContext.Dashboards
                .Include(d => d.Widgets)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            var byId = dash.Widgets.ToDictionary(w => w.Id);
            foreach (var item in dto.Items)
            {
                if (!byId.TryGetValue(item.Id, out var widget)) continue; // only widgets in this dashboard
                widget.X = item.X;
                widget.Y = item.Y;
                widget.W = Math.Clamp(item.W <= 0 ? 1 : item.W, 1, 12);
                widget.H = item.H <= 0 ? 1 : item.H;
            }
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse();
        }

        /// <summary>Remove a widget from a dashboard (owner only).</summary>
        [HttpDelete("{id:guid}/widgets/{widgetId:guid}")]
        public async Task<IActionResult> RemoveWidget(Guid id, Guid widgetId, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var widget = await _dbContext.DashboardWidgets
                .FirstOrDefaultAsync(w => w.Id == widgetId && w.DashboardId == id && w.Dashboard.UserId == userId, ct);
            if (widget is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Widget not found.");

            _dbContext.DashboardWidgets.Remove(widget);
            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse();
        }

        private static DashboardWidgetDto ToWidgetDto(DashboardWidget w) => new()
        {
            Id = w.Id,
            Title = w.Title,
            VizType = w.VizType,
            Plan = JsonDocument.Parse(w.PlanJson).RootElement.Clone(),
            X = w.X,
            Y = w.Y,
            W = w.W,
            H = w.H,
        };
    }
}
