using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public class AiConnectorsController(IConnectorCatalogService catalog, IMyResponseFactory responseFactory) : ControllerBase
    {
        [HttpGet("catalog")]
        public async Task<IActionResult> GetCatalog(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.GetAsync(ct));

        [HttpPut("tool-permissions")]
        public async Task<IActionResult> SetToolPermission([FromBody] ToolPermissionRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.SetToolPermissionAsync(request, ct));

        [HttpPut("installations/{installationId}/enabled")]
        public async Task<IActionResult> SetEnabled(string installationId, [FromBody] SetEnabledRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.SetEnabledAsync(installationId, request.Enabled, ct));

        [HttpDelete("installations/{installationId}")]
        public async Task<IActionResult> Delete(string installationId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await catalog.DeleteAsync(installationId, ct));
    }
}
