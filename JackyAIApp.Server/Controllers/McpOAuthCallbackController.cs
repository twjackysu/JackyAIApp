using JackyAIApp.Server.Services.Connectors;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The two addresses an MCP server's authorization server reaches (docs/ai-native/design/03 §5.2): the
    /// callback a reader is sent back to (a literal route, so it is matched before the account connections'
    /// <c>callback/{provider}</c>, whose flow is unchanged) and our client metadata document, which is our
    /// client id. Neither takes a sign-in: the callback is checked by its state, the document is public.
    /// </summary>
    [ApiController]
    [Route("api/connectors")]
    public class McpOAuthCallbackController(IMcpOAuthFlow oauth, McpOAuthUrlSource urls) : ControllerBase
    {
        [HttpGet("callback/mcp")]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? iss,
            [FromQuery] string? error,
            CancellationToken ct = default)
        {
            var outcome = await oauth.CompleteAsync(code, state, iss, error, ct);
            var page = $"{AuthCallbackController.FrontendBaseUrl(Request)}/connectors";
            var provider = Uri.EscapeDataString(outcome.Provider);
            // kind=mcp: the page tells an AI connector's reader what happened in words of its own.
            return Redirect(outcome.Ok
                ? $"{page}?success=true&provider={provider}&kind=mcp"
                : $"{page}?error={Uri.EscapeDataString(outcome.Error ?? "callback_failed")}&provider={provider}&kind=mcp");
        }

        /// <summary>Client ID Metadata Document: who we are to an authorization server that fetches this URL.</summary>
        [HttpGet("oauth/client-metadata")]
        public IActionResult ClientMetadata() => new JsonResult(McpClientMetadata.Document(urls.Urls));
    }
}
