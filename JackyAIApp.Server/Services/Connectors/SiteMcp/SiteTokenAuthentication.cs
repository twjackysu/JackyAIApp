using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    /// <summary>
    /// Signs a request in by a personal access token (<c>Authorization: Bearer jsk_…</c>), for the site's MCP server
    /// only: the endpoint names this scheme, so the sign-in cookie is never looked at there (a page elsewhere cannot
    /// make a reader's browser call it), and every other endpoint keeps the cookie, so a token opens nothing else.
    /// The principal carries the same <see cref="ClaimTypes.NameIdentifier"/> as the cookie's, so
    /// <see cref="ICurrentUser"/> and every service behind it work as for the reader on the site.
    /// </summary>
    public sealed class SiteTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        ISiteAccessTokenService tokens)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "SiteToken";

        /// <summary>The claim naming the token a request came with.</summary>
        public const string TokenIdClaim = "site_token_id";

        /// <summary>The claim saying the token may use the tools that add or change something ("true").</summary>
        public const string AllowWritesClaim = "site_token_writes";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

            var holder = await tokens.AuthenticateAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
            if (holder == null) return AuthenticateResult.Fail("The token is not valid.");

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, holder.UserId),
                new Claim(TokenIdClaim, holder.TokenId),
                new Claim(AllowWritesClaim, holder.AllowWrites ? "true" : "false"),
            ], SchemeName);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }

        /// <summary>A 401 that says how to authenticate (RFC 6750), and nothing about why a token was refused.</summary>
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            var error = Request.Headers.Authorization.Count > 0 ? ", error=\"invalid_token\"" : "";
            Response.Headers.WWWAuthenticate = $"Bearer realm=\"jacky-studio\"{error}";
            return Task.CompletedTask;
        }

        /// <summary>Who a principal this scheme made stands for, as the MCP server needs it.</summary>
        public static SiteTokenHolder? HolderOf(ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } userId &&
            user.FindFirst(TokenIdClaim)?.Value is { Length: > 0 } tokenId
                ? new SiteTokenHolder(userId, tokenId, user.FindFirst(AllowWritesClaim)?.Value == "true")
                : null;
    }
}
