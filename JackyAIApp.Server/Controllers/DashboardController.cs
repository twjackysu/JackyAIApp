using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// User-owned dashboards built from DataAnalysis summarize results. Each widget stores a
    /// re-runnable summarize plan; the data is re-fetched (via the summarize endpoint) on view.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController(
        ILogger<DashboardController> logger,
        IMyResponseFactory responseFactory,
        AzureSQLDBContext dbContext) : ApiControllerBase
    {
        private readonly ILogger<DashboardController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly AzureSQLDBContext _dbContext = dbContext;

        /// <summary>List the current user's dashboards.</summary>
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var items = await _dbContext.Dashboards
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                .Select(d => new DashboardSummaryDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    WidgetCount = d.Widgets.Count,
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
                CreatedAt = entity.CreatedAt,
            });
        }

        /// <summary>Get a dashboard and its widgets (owner only).</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var dash = await _dbContext.Dashboards
                .Include(d => d.Widgets)
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
            if (dash is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Dashboard not found.");

            return _responseFactory.CreateOKResponse(new DashboardDetailDto
            {
                Id = dash.Id,
                Name = dash.Name,
                Widgets = dash.Widgets
                    .OrderBy(w => w.Y).ThenBy(w => w.X)
                    .Select(ToWidgetDto)
                    .ToList(),
            });
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
