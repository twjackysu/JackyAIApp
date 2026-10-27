using System.Text.Json;
using System.Text.Json.Serialization;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// User-owned dashboards built from DataAnalysis summarize results. Each widget stores a
    /// re-runnable summarize plan; its data is re-fetched on view, authorized by the dashboard's
    /// visibility/shares (<see cref="DashboardAccessPolicy"/>) and run as the dashboard owner, so
    /// shared and custom-source widgets work for viewers.
    /// </summary>
    public interface IDashboardService
    {
        Task<ServiceResult<List<DashboardSummaryDto>>> ListAsync(CancellationToken ct);
        Task<ServiceResult<DashboardSummaryDto>> CreateAsync(CreateDashboardRequest request, CancellationToken ct);
        Task<ServiceResult<DashboardDetailDto>> GetAsync(Guid id, CancellationToken ct);
        Task<ServiceResult<ShareSettingsDto>> GetShareAsync(Guid id, CancellationToken ct);
        Task<ServiceResult<ShareSettingsDto>> UpdateShareAsync(Guid id, UpdateShareRequest request, CancellationToken ct);
        Task<ServiceResult<WidgetDataDto>> GetWidgetDataAsync(Guid id, Guid widgetId, CancellationToken ct);
        Task<ServiceResult<DashboardWidgetDto>> AddWidgetAsync(Guid id, AddWidgetRequest request, CancellationToken ct);
        Task<ServiceResult<NoData>> RenameAsync(Guid id, RenameDashboardRequest request, CancellationToken ct);
        Task<ServiceResult<NoData>> DeleteAsync(Guid id, CancellationToken ct);
        Task<ServiceResult<NoData>> UpdateLayoutAsync(Guid id, UpdateLayoutRequest request, CancellationToken ct);
        Task<ServiceResult<NoData>> RemoveWidgetAsync(Guid id, Guid widgetId, CancellationToken ct);
    }

    public class DashboardService(
        ILogger<DashboardService> logger,
        AzureSQLDBContext dbContext,
        EngineSources engines,
        ICurrentUser currentUser) : IDashboardService
    {
        private static readonly JsonSerializerOptions PlanJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // Not Forbidden: a dashboard you may not see answers as if it didn't exist, so a private
        // one's existence isn't leaked.
        private static ServiceFailure DashboardNotFound => ServiceResult.Fail(ErrorCodes.NotFound, "Dashboard not found.");

        /// <summary>The dashboards the user owns or has been granted access to (restricted shares).</summary>
        public async Task<ServiceResult<List<DashboardSummaryDto>>> ListAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var items = await dbContext.Dashboards
                .Where(DashboardAccessPolicy.ListedFor(userId))
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
            return ServiceResult.Ok(items);
        }

        /// <summary>A new, empty dashboard.</summary>
        public async Task<ServiceResult<DashboardSummaryDto>> CreateAsync(CreateDashboardRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Name is required.");

            var entity = new Dashboard
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.Dashboards.Add(entity);
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("Created dashboard {id} for user {uid}", entity.Id, userId);

            return ServiceResult.Ok(new DashboardSummaryDto
            {
                Id = entity.Id,
                Name = entity.Name,
                WidgetCount = 0,
                Visibility = entity.Visibility,
                IsOwner = true,
                CreatedAt = entity.CreatedAt,
            });
        }

        /// <summary>A dashboard and its widgets: for the owner, or a viewer its visibility allows
        /// (anonymous ones included, for Anyone).</summary>
        public async Task<ServiceResult<DashboardDetailDto>> GetAsync(Guid id, CancellationToken ct)
        {
            var userId = currentUser.Id; // may be null for anonymous (allowed only for Anyone)

            var dash = await dbContext.Dashboards
                .Include(d => d.Widgets)
                .Include(d => d.Shares)
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null || !DashboardAccessPolicy.CanView(dash, userId))
                return DashboardNotFound;

            return ServiceResult.Ok(new DashboardDetailDto
            {
                Id = dash.Id,
                Name = dash.Name,
                Visibility = dash.Visibility,
                IsOwner = userId != null && dash.UserId == userId,
                Widgets = dash.Widgets
                    .OrderBy(w => w.Y).ThenBy(w => w.X)
                    .Select(ToWidgetDto)
                    .ToList(),
            });
        }

        /// <summary>Sharing settings (owner only).</summary>
        public async Task<ServiceResult<ShareSettingsDto>> GetShareAsync(Guid id, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var dash = await dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            return dash is null ? DashboardNotFound : ServiceResult.Ok(ToShareDto(dash));
        }

        /// <summary>Sets the visibility and, for Restricted, the full set of allowed users by email (owner only).</summary>
        public async Task<ServiceResult<ShareSettingsDto>> UpdateShareAsync(Guid id, UpdateShareRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (!DashboardAccessPolicy.TryParseVisibility(request.Visibility, out var visibility))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Invalid visibility.");

            var dash = await dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null)
                return DashboardNotFound;

            dash.Visibility = visibility;

            if (visibility == DashboardVisibility.Restricted)
            {
                var emails = (request.Emails ?? [])
                    .Select(e => e.Trim())
                    .Where(e => e.Length > 0)
                    .ToList();
                var users = emails.Count == 0
                    ? []
                    : await dbContext.Users
                        .Where(u => u.Email != null && emails.Contains(u.Email))
                        .ToListAsync(ct);
                // Owner always has access — never store a share for them.
                var desiredIds = users.Select(u => u.Id).Where(uid => uid != userId).ToHashSet();

                dbContext.DashboardShares.RemoveRange(
                    dash.Shares.Where(s => !desiredIds.Contains(s.UserId)));
                var existingIds = dash.Shares.Select(s => s.UserId).ToHashSet();
                foreach (var uid in desiredIds.Where(uid => !existingIds.Contains(uid)))
                {
                    dbContext.DashboardShares.Add(new DashboardShare
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
                // Private / Public / Anyone — shares are irrelevant; clear them.
                dbContext.DashboardShares.RemoveRange(dash.Shares);
            }

            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            // Re-load to return the resolved members.
            var fresh = await dbContext.Dashboards
                .Include(d => d.Shares).ThenInclude(s => s.User)
                .FirstAsync(d => d.Id == id, ct);
            return ServiceResult.Ok(ToShareDto(fresh));
        }

        private static ShareSettingsDto ToShareDto(Dashboard dash) => new()
        {
            Visibility = dash.Visibility,
            Members = dash.Shares
                .Select(s => new ShareMemberDto { Email = s.User?.Email })
                .ToList(),
        };

        /// <summary>
        /// Re-runs a widget's saved plan and returns its rows. Access is gated by the dashboard's
        /// visibility/shares (anonymous allowed only for Anyone); the engine is resolved as the
        /// dashboard OWNER, so custom-source widgets work for any allowed viewer.
        /// </summary>
        public async Task<ServiceResult<WidgetDataDto>> GetWidgetDataAsync(Guid id, Guid widgetId, CancellationToken ct)
        {
            var userId = currentUser.Id; // may be null (allowed only for Anyone)

            var dash = await dbContext.Dashboards
                .Include(d => d.Shares)
                .Include(d => d.Widgets)
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null || !DashboardAccessPolicy.CanView(dash, userId))
                return DashboardNotFound;

            var widget = dash.Widgets.FirstOrDefault(w => w.Id == widgetId);
            if (widget is null)
                return ServiceResult.Fail(ErrorCodes.NotFound, "Widget not found.");

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
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Invalid widget plan.");

            // Resolved as the dashboard OWNER (not the viewer), so a custom-source widget works for any
            // allowed viewer, through the same path a live DataAnalysis query takes.
            var (source, failure) = await engines.ResolveAsync(plan.Context, dash.UserId, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            // The saved plan is the owner's input replayed for any viewer (anonymous for Anyone), so it
            // passes the same schema gate as a live DataAnalysis query.
            if (await engines.GuardAsync(source, fields => DataAnalysisFieldGuard.Check(fields, plan), ct) is { } rejected)
                return rejected;

            var result = await engines.SummarizeAsync(source, plan, ct);
            if (result.IsSuccess)
                return ServiceResult.Ok(new WidgetDataDto { Rows = result.Value!.Rows.Select(r => (object)r.Data).ToList() });

            // The driver's text names servers, tables and SQL. Only the source's owner looking at their own
            // dashboard gets it, to debug their database; any other viewer — anonymous ones on an Anyone
            // dashboard included — gets the generic message, and the detail stays in the log.
            return engines.QueryFailed(source, result.ErrorMessage, "Query failed.", viewerIsOwner: userId == dash.UserId);
        }

        /// <summary>Adds a widget (owner only), placed below the existing ones.</summary>
        public async Task<ServiceResult<DashboardWidgetDto>> AddWidgetAsync(Guid id, AddWidgetRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (string.IsNullOrWhiteSpace(request.VizType))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "VizType is required.");

            var dash = await dbContext.Dashboards
                .Include(d => d.Widgets)
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null)
                return DashboardNotFound;

            var w = Math.Clamp(request.W <= 0 ? 6 : request.W, 1, 12);
            var h = request.H <= 0 ? 8 : request.H;
            var nextY = dash.Widgets.Count == 0 ? 0 : dash.Widgets.Max(x => x.Y + x.H);

            var widget = new DashboardWidget
            {
                Id = Guid.NewGuid(),
                DashboardId = dash.Id,
                Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title!.Trim(),
                VizType = request.VizType,
                PlanJson = request.Plan.GetRawText(),
                X = 0,
                Y = nextY,
                W = w,
                H = h,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            dbContext.DashboardWidgets.Add(widget);
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            return ServiceResult.Ok(ToWidgetDto(widget));
        }

        /// <summary>Renames a dashboard (owner only).</summary>
        public async Task<ServiceResult<NoData>> RenameAsync(Guid id, RenameDashboardRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Name is required.");

            var dash = await dbContext.Dashboards
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null)
                return DashboardNotFound;

            dash.Name = name;
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }

        /// <summary>Deletes a dashboard and its widgets (owner only).</summary>
        public async Task<ServiceResult<NoData>> DeleteAsync(Guid id, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var dash = await dbContext.Dashboards
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null)
                return DashboardNotFound;

            dbContext.Dashboards.Remove(dash); // widgets cascade
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }

        /// <summary>Stores the grid positions/sizes of the dashboard's widgets (owner only).</summary>
        public async Task<ServiceResult<NoData>> UpdateLayoutAsync(Guid id, UpdateLayoutRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var dash = await dbContext.Dashboards
                .Include(d => d.Widgets)
                .Where(DashboardAccessPolicy.OwnedBy(userId))
                .FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dash is null)
                return DashboardNotFound;

            var byId = dash.Widgets.ToDictionary(w => w.Id);
            foreach (var item in request.Items)
            {
                if (!byId.TryGetValue(item.Id, out var widget)) continue; // only widgets in this dashboard
                widget.X = item.X;
                widget.Y = item.Y;
                widget.W = Math.Clamp(item.W <= 0 ? 1 : item.W, 1, 12);
                widget.H = item.H <= 0 ? 1 : item.H;
            }
            dash.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok();
        }

        /// <summary>Removes a widget (owner only).</summary>
        public async Task<ServiceResult<NoData>> RemoveWidgetAsync(Guid id, Guid widgetId, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var widget = await dbContext.DashboardWidgets
                .FirstOrDefaultAsync(w => w.Id == widgetId && w.DashboardId == id && w.Dashboard.UserId == userId, ct);
            if (widget is null)
                return ServiceResult.Fail(ErrorCodes.NotFound, "Widget not found.");

            dbContext.DashboardWidgets.Remove(widget);
            await dbContext.SaveChangesAsync(ct);
            return ServiceResult.Ok();
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
