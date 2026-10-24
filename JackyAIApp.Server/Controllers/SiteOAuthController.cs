using System.Text.Json.Nodes;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The site as the OAuth authorization server of its own MCP server (<see cref="ISiteOAuthServer"/>): the metadata
    /// documents and the registration and token endpoints answer in OAuth's own JSON (anonymous: a client calls them);
    /// the consent page's two calls are the reader's, by cookie, in the app's envelope. The page itself is the client's
    /// <c>/oauth/authorize</c>.
    /// </summary>
    [ApiController]
    public class SiteOAuthController(ISiteOAuthServer oauth, IOptions<ConnectorOptions> options, IMyResponseFactory responseFactory)
        : ControllerBase
    {
        private SiteOAuthUrls Urls => SiteOAuthUrls.From(options.Value, $"{Request.Scheme}://{Request.Host}");

        /// <summary>RFC 9728, at the root and path-inserted for <c>/api/mcp</c> (clients look for either).</summary>
        [HttpGet("/.well-known/oauth-protected-resource")]
        [HttpGet("/.well-known/oauth-protected-resource/api/mcp")]
        public IActionResult ProtectedResource() => Json(StatusCodes.Status200OK, oauth.ProtectedResourceMetadata(Urls));

        /// <summary>RFC 8414.</summary>
        [HttpGet("/.well-known/oauth-authorization-server")]
        public IActionResult AuthorizationServer() => Json(StatusCodes.Status200OK, oauth.AuthorizationServerMetadata(Urls));

        [HttpPost("/api/oauth/register")]
        [EnableRateLimiting(RateLimitPolicies.SiteOAuth)]
        [RequestSizeLimit(16_000)]
        public IActionResult Register([FromBody] SiteOAuthRegisterRequestDto request) => Reply(oauth.Register(request));

        [HttpPost("/api/oauth/token")]
        [EnableRateLimiting(RateLimitPolicies.SiteOAuth)]
        [Consumes("application/x-www-form-urlencoded")]
        [RequestSizeLimit(16_000)]
        public async Task<IActionResult> Token([FromForm] SiteOAuthTokenRequestDto request, CancellationToken ct = default)
        {
            Response.Headers.CacheControl = "no-store";
            return Reply(await oauth.TokenAsync(request, Urls, ct));
        }

        /// <summary>What the consent page shows for a request.</summary>
        [Authorize]
        [HttpGet("/api/oauth/consent")]
        public IActionResult Consent([FromQuery] SiteOAuthAuthorizeRequestDto request) =>
            responseFactory.ToActionResult(oauth.Consent(request, Urls));

        /// <summary>The reader's answer: where to send them.</summary>
        [Authorize]
        [HttpPost("/api/oauth/consent")]
        public IActionResult Decide([FromBody] SiteOAuthDecisionDto decision) =>
            responseFactory.ToActionResult(oauth.Decide(decision, Urls));

        private IActionResult Reply(SiteOAuthReply reply) => Json(reply.StatusCode, reply.Body);

        private static ContentResult Json(int status, JsonObject body) =>
            new() { StatusCode = status, ContentType = "application/json", Content = body.ToJsonString() };
    }
}
