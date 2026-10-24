using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The reader's personal access tokens for the site's MCP server, on the connectors page (signed in by the cookie,
    /// as every page is: a token cannot make more tokens). In the <c>{ data }</c> envelope.
    /// </summary>
    [ApiController]
    [Route("api/connectors/site-tokens")]
    [Authorize]
    public class SiteAccessTokensController(ISiteAccessTokenService tokens, IMyResponseFactory responseFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await tokens.ListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSiteAccessTokenRequestDto request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await tokens.CreateAsync(request, ct));

        [HttpDelete("{tokenId}")]
        public async Task<IActionResult> Revoke(string tokenId, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await tokens.RevokeAsync(tokenId, ct));
    }
}
