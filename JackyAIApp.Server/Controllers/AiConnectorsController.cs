using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The connectors the AI may use (docs/ai-native/design/03 §7), beside the account connections of
    /// <see cref="ConnectorsController"/> under the same route, which stay as they are. These answer in
    /// the <c>{ data }</c> envelope.
    /// </summary>
    [ApiController]
    [Route("api/connectors")]
    [Authorize]
    public class AiConnectorsController(
        IConnectorCatalogService catalog, IConnectorInstallService installs, IMcpOAuthFlow oauth, IMcpRegistry registry,
        IMcpAppService apps, IMyResponseFactory responseFactory)
        : ControllerBase
    {
        [HttpGet("catalog")]
        public async Task<IActionResult> GetCatalog(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.GetAsync(ct));

        /// <summary>Remote MCP servers in the official MCP Registry whose name holds the search (D-122); saves nothing.</summary>
        [HttpGet("registry")]
        public async Task<IActionResult> SearchRegistry([FromQuery] string? search, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await registry.SearchAsync(search, ct));

        /// <summary>Looks at an MCP server's URL; saves nothing.</summary>
        [HttpPost("installations/probe")]
        [EnableRateLimiting(RateLimitPolicies.ConnectorProbe)]
        public async Task<IActionResult> Probe([FromBody] ProbeRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await installs.ProbeAsync(request.ServerUrl, ct));

        [HttpPost("installations")]
        [EnableRateLimiting(RateLimitPolicies.ConnectorProbe)]
        public async Task<IActionResult> Create([FromBody] CreateInstallationRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await installs.CreateAsync(request, ct));

        [HttpPost("installations/{installationId}/test")]
        [EnableRateLimiting(RateLimitPolicies.ConnectorProbe)]
        public async Task<IActionResult> Test(string installationId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await installs.TestAsync(installationId, ct));

        /// <summary>Starts signing in to an OAuth connector: where to send the reader.</summary>
        [HttpPost("installations/{installationId}/authorize")]
        public async Task<IActionResult> Authorize(string installationId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await oauth.StartAsync(installationId, ct));

        [HttpPut("tool-permissions")]
        public async Task<IActionResult> SetToolPermission([FromBody] ToolPermissionRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.SetToolPermissionAsync(request, ct));

        [HttpPut("installations/{installationId}/enabled")]
        public async Task<IActionResult> SetEnabled(string installationId, [FromBody] SetEnabledRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.SetEnabledAsync(installationId, request.Enabled, ct));

        /// <summary>
        /// The MCP App a tool of the reader's connector named (<c>ui://…</c>), for the chat to draw its card
        /// (docs/ai-native/design/09 §3); read from that server each time (the client keeps it a while).
        /// </summary>
        [HttpGet("installations/{installationId}/app")]
        [EnableRateLimiting(RateLimitPolicies.McpApp)]
        public async Task<IActionResult> GetApp(string installationId, [FromQuery] string? uri, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await apps.GetAsync(installationId, uri, ct));

        /// <summary>
        /// A call a connector's app asks of its server (design/09 §3): a tool that only reads and runs on its own, or one for
        /// the app alone the reader allowed on its card (D-207); its result for the app alone; no AI, no charge.
        /// </summary>
        [HttpPost("installations/{installationId}/app-call")]
        [EnableRateLimiting(RateLimitPolicies.McpAppCall)]
        [RequestSizeLimit(McpToolClient.MaxAppResultChars)]
        public async Task<IActionResult> CallFromApp(string installationId, [FromBody] McpAppCallRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await apps.CallAsync(installationId, request.Tool, request.Arguments, request.ReaderAllowed, ct));

        [HttpDelete("installations/{installationId}")]
        public async Task<IActionResult> Delete(string installationId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.DeleteAsync(installationId, ct));
    }
}
