using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// User-owned dashboards built from DataAnalysis summarize results; see <see cref="IDashboardService"/>.
    /// Get and the widget data allow anonymous requests, for dashboards visible to Anyone.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController(IMyResponseFactory responseFactory, IDashboardService dashboards) : ControllerBase
    {
        /// <summary>List dashboards the user owns or has been granted access to (restricted shares).</summary>
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.ListAsync(ct));

        /// <summary>Create a new (empty) dashboard.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDashboardRequest dto, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.CreateAsync(dto, ct));

        /// <summary>Get a dashboard and its widgets (owner, or a viewer allowed by visibility — incl. anonymous for "Anyone").</summary>
        [AllowAnonymous]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.GetAsync(id, ct));

        /// <summary>Get sharing settings for a dashboard (owner only).</summary>
        [HttpGet("{id:guid}/share")]
        public async Task<IActionResult> GetShare(Guid id, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.GetShareAsync(id, ct));

        /// <summary>Set visibility and, for Restricted, the full set of allowed users by email (owner only).</summary>
        [HttpPut("{id:guid}/share")]
        public async Task<IActionResult> UpdateShare(Guid id, [FromBody] UpdateShareRequest dto, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.UpdateShareAsync(id, dto, ct));

        /// <summary>Re-run a widget's saved plan and return its rows (anonymous allowed only for "Anyone").</summary>
        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.DashboardWidgetData)]
        [HttpGet("{id:guid}/widgets/{widgetId:guid}/data")]
        public async Task<IActionResult> WidgetData(Guid id, Guid widgetId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.GetWidgetDataAsync(id, widgetId, ct));

        /// <summary>Add a widget to a dashboard (owner only). Auto-placed below existing widgets.</summary>
        [HttpPost("{id:guid}/widgets")]
        public async Task<IActionResult> AddWidget(Guid id, [FromBody] AddWidgetRequest dto, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.AddWidgetAsync(id, dto, ct));

        /// <summary>Rename a dashboard (owner only).</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Rename(Guid id, [FromBody] RenameDashboardRequest dto, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.RenameAsync(id, dto, ct));

        /// <summary>Delete a dashboard and its widgets (owner only).</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.DeleteAsync(id, ct));

        /// <summary>Persist the grid positions/sizes of a dashboard's widgets (owner only).</summary>
        [HttpPut("{id:guid}/layout")]
        public async Task<IActionResult> UpdateLayout(Guid id, [FromBody] UpdateLayoutRequest dto, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.UpdateLayoutAsync(id, dto, ct));

        /// <summary>Remove a widget from a dashboard (owner only).</summary>
        [HttpDelete("{id:guid}/widgets/{widgetId:guid}")]
        public async Task<IActionResult> RemoveWidget(Guid id, Guid widgetId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await dashboards.RemoveWidgetAsync(id, widgetId, ct));
    }
}
