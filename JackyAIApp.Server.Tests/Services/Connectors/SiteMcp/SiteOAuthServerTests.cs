using System.Security.Claims;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Services.Connectors.SiteMcp
{
    /// <summary>
    /// A whole OAuth sign-in to the site's MCP server, as Claude.ai or ChatGPT would do it: register, the reader's
    /// consent, the code for a token, and that token working at the MCP server; and each check on the way.
    /// </summary>
    public class SiteOAuthServerTests : IDisposable
    {
        private const string User = "u1";
        private const string Redirect = "https://claude.ai/api/mcp/auth_callback";
        // RFC 7636 appendix B.
        private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        private static readonly SiteOAuthUrls Urls = new("https://apps.jacky-studio.com");

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
        private readonly SiteOAuthClients _clients = new(new EphemeralDataProtectionProvider());
        private readonly SiteOAuthCodes _codes = new(new MemoryCache(new MemoryCacheOptions()));

        public void Dispose() => _db.Dispose();

        private SiteAccessTokenService Tokens(string? userId)
        {
            var http = new DefaultHttpContext();
            if (userId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
            return new SiteAccessTokenService(_db, TestCurrentUser.Of(http), _clock);
        }

        private SiteOAuthServer Server(string? userId = User)
        {
            var http = new DefaultHttpContext();
            if (userId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
            return new SiteOAuthServer(_clients, _codes, Tokens(userId), TestCurrentUser.Of(http));
        }

        private string RegisterClient(string name = "Claude")
        {
            var reply = Server(null).Register(new SiteOAuthRegisterRequestDto { ClientName = name, RedirectUris = [Redirect] });
            Assert.Equal(StatusCodes.Status201Created, reply.StatusCode);
            return (string)reply.Body["client_id"]!;
        }

        private static SiteOAuthAuthorizeRequestDto Authorize(string clientId, string? scope = "mcp:read") => new()
        {
            ResponseType = "code",
            ClientId = clientId,
            RedirectUri = Redirect,
            CodeChallenge = Challenge,
            CodeChallengeMethod = "S256",
            State = "xyz",
            Scope = scope,
            Resource = "https://apps.jacky-studio.com/api/mcp",
        };

        private static Dictionary<string, string> QueryOf(string url) =>
            QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(p => p.Key, p => p.Value.ToString());

        private string ApproveAndGetCode(string clientId, bool allowWrites = false)
        {
            var decided = Server().Decide(new SiteOAuthDecisionDto { Request = Authorize(clientId), Approve = true, AllowWrites = allowWrites }, Urls);
            Assert.True(decided.IsSuccess);
            var query = QueryOf(decided.Data.RedirectUrl);
            Assert.StartsWith(Redirect + "?", decided.Data.RedirectUrl);
            Assert.Equal("xyz", query["state"]);
            Assert.Equal("https://apps.jacky-studio.com", query["iss"]);
            return query["code"];
        }

        private static SiteOAuthTokenRequestDto TokenRequest(string clientId, string code, string verifier = Verifier) => new()
        {
            GrantType = "authorization_code",
            Code = code,
            ClientId = clientId,
            RedirectUri = Redirect,
            CodeVerifier = verifier,
            Resource = "https://apps.jacky-studio.com/api/mcp",
        };

        [Fact]
        public void TheMetadata_PointsAClientToEveryStep()
        {
            var prm = Server(null).ProtectedResourceMetadata(Urls);
            var asm = Server(null).AuthorizationServerMetadata(Urls);

            Assert.Equal("https://apps.jacky-studio.com/api/mcp", (string?)prm["resource"]);
            Assert.Equal("https://apps.jacky-studio.com", (string?)prm["authorization_servers"]![0]);
            Assert.Equal("https://apps.jacky-studio.com", (string?)asm["issuer"]);
            Assert.Equal("https://apps.jacky-studio.com/oauth/authorize", (string?)asm["authorization_endpoint"]);
            Assert.Equal("https://apps.jacky-studio.com/api/oauth/token", (string?)asm["token_endpoint"]);
            Assert.Equal("https://apps.jacky-studio.com/api/oauth/register", (string?)asm["registration_endpoint"]);
            Assert.Equal("S256", (string?)asm["code_challenge_methods_supported"]![0]);
        }

        [Fact]
        public void TheUrls_ComeFromTheSitesPublicAddress_ElseTheRequest()
        {
            Assert.Equal("https://apps.jacky-studio.com", SiteOAuthUrls.From(
                new() { CallbackBaseUrl = "https://apps.jacky-studio.com/api/connectors/callback" }, "http://internal:8080").Issuer);
            Assert.Equal("https://localhost:7034", SiteOAuthUrls.From(new(), "https://localhost:7034/").Issuer);
        }

        [Fact]
        public async Task AWholeSignIn_EndsInATokenTheMcpServerTakes()
        {
            var clientId = RegisterClient();

            var consent = Server().Consent(Authorize(clientId, "mcp:read mcp:write"), Urls);
            Assert.Equal(new SiteOAuthConsentDto("Claude", "claude.ai", WantsWrites: true), consent.Data);

            var code = ApproveAndGetCode(clientId, allowWrites: false);
            var reply = await Server(null).TokenAsync(TokenRequest(clientId, code), Urls, CancellationToken.None);

            Assert.Equal(StatusCodes.Status200OK, reply.StatusCode);
            Assert.Equal("Bearer", (string?)reply.Body["token_type"]);
            Assert.Equal("mcp:read", (string?)reply.Body["scope"]);
            Assert.Equal(SiteOAuthServer.TokenLifetimeDays * 86400, (int)reply.Body["expires_in"]!);
            var holder = await Tokens(null).AuthenticateAsync((string)reply.Body["access_token"]!, CancellationToken.None);
            Assert.NotNull(holder);
            Assert.Equal(User, holder.UserId);
            Assert.False(holder.AllowWrites); // the reader's choice, not the scope the client asked for
            var row = Assert.Single(_db.SiteAccessTokens);
            Assert.Equal("Claude", row.Name);
            Assert.True(row.ViaOAuth);
            Assert.Equal(_clock.Now.UtcDateTime.AddDays(SiteOAuthServer.TokenLifetimeDays), row.ExpiresAt);
        }

        [Fact]
        public async Task ACode_IsSpentByTheFirstTry_EvenAFailedOne()
        {
            var clientId = RegisterClient();
            var code = ApproveAndGetCode(clientId);

            var wrong = await Server(null).TokenAsync(TokenRequest(clientId, code, Verifier[..^1] + "l"), Urls, CancellationToken.None);
            var again = await Server(null).TokenAsync(TokenRequest(clientId, code), Urls, CancellationToken.None);

            Assert.Equal("invalid_grant", (string?)wrong.Body["error"]);
            Assert.Equal("invalid_grant", (string?)again.Body["error"]);
            Assert.Empty(_db.SiteAccessTokens);
        }

        [Fact]
        public async Task ACode_IsOnlyForTheClientAndRedirectItWasIssuedTo()
        {
            var clientId = RegisterClient();
            var other = RegisterClient("Other");

            var stolen = await Server(null).TokenAsync(TokenRequest(other, ApproveAndGetCode(clientId)), Urls, CancellationToken.None);
            var moved = TokenRequest(clientId, ApproveAndGetCode(clientId));
            moved.RedirectUri = "https://claude.ai/elsewhere";

            Assert.Equal("invalid_grant", (string?)stolen.Body["error"]);
            Assert.Equal("invalid_grant", (string?)(await Server(null).TokenAsync(moved, Urls, CancellationToken.None)).Body["error"]);
        }

        [Fact]
        public async Task OnlyTheCodeGrant_IsTaken()
        {
            var reply = await Server(null).TokenAsync(new SiteOAuthTokenRequestDto { GrantType = "client_credentials" }, Urls, CancellationToken.None);

            Assert.Equal("unsupported_grant_type", (string?)reply.Body["error"]);
        }

        [Fact]
        public async Task ATokenForAnotherResource_IsRefused()
        {
            var clientId = RegisterClient();
            var request = TokenRequest(clientId, ApproveAndGetCode(clientId));
            request.Resource = "https://evil.example/mcp";

            Assert.Equal("invalid_target", (string?)(await Server(null).TokenAsync(request, Urls, CancellationToken.None)).Body["error"]);
        }

        [Fact]
        public void Declining_GoesBackWithAccessDenied_AndNoCode()
        {
            var clientId = RegisterClient();

            var decided = Server().Decide(new SiteOAuthDecisionDto { Request = Authorize(clientId), Approve = false }, Urls);

            var query = QueryOf(decided.Data!.RedirectUrl);
            Assert.Equal("access_denied", query["error"]);
            Assert.Equal("xyz", query["state"]);
            Assert.False(query.ContainsKey("code"));
        }

        public static TheoryData<string> BadRequests => new() { "client", "redirect", "responseType", "pkce", "resource" };

        [Theory]
        [MemberData(nameof(BadRequests))]
        public void ARequestThatDoesNotCheckOut_IsShownOnTheSite_NeverSentBack(string broken)
        {
            var request = Authorize(RegisterClient());
            switch (broken)
            {
                case "client": request.ClientId = "made-up"; break;
                case "redirect": request.RedirectUri = "https://evil.example/cb"; break;
                case "responseType": request.ResponseType = "token"; break;
                case "pkce": request.CodeChallengeMethod = "plain"; break;
                case "resource": request.Resource = "https://evil.example/mcp"; break;
            }

            Assert.Equal(ErrorCodes.BadRequest, Server().Consent(request, Urls).Error);
            Assert.Equal(ErrorCodes.BadRequest, Server().Decide(new SiteOAuthDecisionDto { Request = request, Approve = true }, Urls).Error);
        }

        [Fact]
        public void ARegistration_ThatIsNotAPublicClient_OrHasABadRedirect_IsRefused()
        {
            var secret = Server(null).Register(new SiteOAuthRegisterRequestDto { RedirectUris = [Redirect], TokenEndpointAuthMethod = "client_secret_basic" });
            var bad = Server(null).Register(new SiteOAuthRegisterRequestDto { RedirectUris = ["http://evil.example/cb"] });

            Assert.Equal("invalid_client_metadata", (string?)secret.Body["error"]);
            Assert.Equal("invalid_redirect_uri", (string?)bad.Body["error"]);
            Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        }

        [Fact]
        public void ARegistration_WhoseRedirectsComeToTooMuch_IsRefused_NotAFailure()
        {
            // Ten addresses each within the limit, escaped to far more in the client id (& → \u0026).
            var uris = Enumerable.Range(0, 10).Select(i => $"https://a.example/{i}?" + new string('&', 450)).ToList();

            var reply = Server(null).Register(new SiteOAuthRegisterRequestDto { RedirectUris = uris });

            Assert.Equal("invalid_redirect_uri", (string?)reply.Body["error"]);
            Assert.Equal(StatusCodes.Status400BadRequest, reply.StatusCode);
        }

        [Fact]
        public async Task AReaderWithTooManyTokens_GetsNoMore()
        {
            var clientId = RegisterClient();
            for (var i = 0; i < SiteAccessTokenService.Limit; i++)
                await Tokens(User).CreateAsync(new CreateSiteAccessTokenRequestDto { Name = $"t{i}" }, CancellationToken.None);

            var reply = await Server(null).TokenAsync(TokenRequest(clientId, ApproveAndGetCode(clientId)), Urls, CancellationToken.None);

            Assert.Equal("invalid_grant", (string?)reply.Body["error"]);
        }
    }
}
