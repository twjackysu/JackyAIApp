using System.Text.Json;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The site's own MCP server over Streamable HTTP (<see cref="ISiteMcpServer"/>): one JSON-RPC message a POST,
    /// answered with JSON. Signed in by a personal access token only (the scheme is named, so the sign-in cookie is
    /// not read here).
    /// </summary>
    [ApiController]
    [Route("api/mcp")]
    [Authorize(AuthenticationSchemes = SiteTokenAuthenticationHandler.SchemeName)]
    [EnableRateLimiting(RateLimitPolicies.SiteMcp)]
    public class SiteMcpController(ISiteMcpServer server, IOptions<ConnectorOptions> options) : ControllerBase
    {
        /// <summary>The largest message taken; a tool's arguments are a few short fields.</summary>
        public const int MaxMessageBytes = 1_000_000;

        private const int ParseError = -32700;

        [HttpPost]
        [RequestSizeLimit(MaxMessageBytes)]
        public async Task<IActionResult> Post(CancellationToken ct = default)
        {
            // The transport asks servers to check Origin (DNS rebinding); a page of another site has no business here.
            if (!SameOriginOrNone()) return StatusCode(StatusCodes.Status403Forbidden);

            JsonNode? message;
            try
            {
                message = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
            }
            catch (JsonException)
            {
                return Reply(SiteMcpServer.Error(null, ParseError, "Parse error."));
            }

            var holder = SiteTokenAuthenticationHandler.HolderOf(User) ?? throw new NotSignedInException();
            var origin = SiteOAuthUrls.From(options.Value, $"{Request.Scheme}://{Request.Host}").Issuer;
            return Reply(await server.HandleAsync(message, holder, origin, ct));
        }

        /// <summary>No stream to open and no session to end: this server answers each POST on its own.</summary>
        [HttpGet]
        [HttpDelete]
        public IActionResult NotAllowed()
        {
            Response.Headers.Allow = "POST";
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        private bool SameOriginOrNone()
        {
            var origin = Request.Headers.Origin.ToString();
            return origin.Length == 0 ||
                (Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase));
        }

        private IActionResult Reply(SiteMcpReply reply) => reply.Body == null
            ? StatusCode(reply.StatusCode)
            : new ContentResult { StatusCode = reply.StatusCode, ContentType = "application/json", Content = reply.Body.ToJsonString() };
    }
}
