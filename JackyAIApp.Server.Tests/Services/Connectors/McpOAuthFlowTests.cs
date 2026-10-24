using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// Signing in to an MCP server: becoming its authorization server's client, sending the reader there,
    /// taking the code back for tokens, and refreshing them.
    /// </summary>
    public class McpOAuthFlowTests : IDisposable
    {
        private const string User = "u1";
        private static readonly Uri Url = new("https://mcp.example.com/mcp");
        private static readonly McpOAuthUrls SiteUrls = new("https://apps.example.com/api/connectors/callback/mcp",
            "https://apps.example.com/api/connectors/oauth/client-metadata");

        private static readonly McpOAuthServer AuthServer = new("https://mcp.example.com/mcp", "https://auth.example.com",
            new Uri("https://auth.example.com/authorize?tenant=t1"), new Uri("https://auth.example.com/token"), new Uri("https://auth.example.com/register"),
            ClientIdMetadataDocumentSupported: false, IssParameterSupported: true, Scope: "notes:read");

        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        private readonly AzureSQLDBContext _db;
        private readonly Mock<IMcpOAuthDiscovery> _discovery = new();
        private readonly Mock<IMcpOAuthEndpoints> _endpoints = new();
        private readonly Mock<IMcpToolClient> _mcp = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly FakeStates _states = new();
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero));
        private readonly ConnectorSecrets _secrets;

        /// <summary>States that are valid when this issued them, for the user and provider it issued them for.</summary>
        private sealed class FakeStates : IStateService
        {
            private readonly Dictionary<string, (string UserId, string Provider)> _issued = new();

            public string GenerateState(string userId, string provider)
            {
                var state = $"state-{_issued.Count + 1}";
                _issued[state] = (userId, provider);
                return state;
            }

            public bool ValidateState(string state, out string userId, out string provider)
            {
                var found = _issued.TryGetValue(state, out var issued);
                (userId, provider) = found ? issued : ("", "");
                return found;
            }
        }

        public McpOAuthFlowTests()
        {
            _db = new AzureSQLDBContext(_options);
            var encryption = new Mock<ITokenEncryptionService>();
            encryption.Setup(e => e.EncryptToken(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
            encryption.Setup(e => e.DecryptToken(It.IsAny<string>())).Returns<string>(s => s["enc:".Length..]);
            _secrets = new ConnectorSecrets(encryption.Object, NullLogger<ConnectorSecrets>.Instance);
            _discovery.Setup(d => d.ChallengeAsync(Url, It.IsAny<CancellationToken>())).ReturnsAsync(new McpAuthChallenge(null, "notes:read"));
            _discovery.Setup(d => d.DiscoverAsync(Url, It.IsAny<McpAuthChallenge>(), It.IsAny<CancellationToken>())).ReturnsAsync(AuthServer);
        }

        public void Dispose()
        {
            _db.Dispose();
            _cache.Dispose();
        }

        private McpOAuthFlow Flow(string? signedIn = User)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(signedIn);
            return new McpOAuthFlow(_db, user.Object, _states, _cache, _discovery.Object, _endpoints.Object, _mcp.Object, _secrets,
                UrlSource(), _clock, NullLogger<McpOAuthFlow>.Instance);
        }

        private static McpOAuthUrlSource UrlSource() => new(Microsoft.Extensions.Options.Options.Create(
            new JackyAIApp.Server.Configuration.ConnectorOptions { CallbackBaseUrl = "https://apps.example.com/api/connectors/callback" }));

        private McpOAuthTokens Tokens() =>
            new(new TestDbContextFactory(_options), _secrets, _endpoints.Object, _clock, NullLogger<McpOAuthTokens>.Instance);

        private string Installed(McpOAuthGrant grant, string userId = User)
        {
            var row = new SQL.ConnectorInstallation
            {
                UserId = userId, DefinitionKey = SQL.ConnectorInstallation.CustomKey, DisplayName = "Notes", ServerUrl = Url.ToString(),
                AuthKind = "OAuth", LastStatus = "NeedsAuth", EncryptedSecret = _secrets.Seal(grant),
            };
            _db.ConnectorInstallations.Add(row);
            _db.SaveChanges();
            return row.Id;
        }

        private SQL.ConnectorInstallation Row(string id)
        {
            using var db = new AzureSQLDBContext(_options);
            return db.ConnectorInstallations.AsNoTracking().Single(c => c.Id == id);
        }

        // ── becoming a client ───────────────────────────────────────────────────────────────────

        [Fact]
        public async Task WithARegistrationEndpoint_WeRegisterAsAPublicClient_ForOurCallback()
        {
            _endpoints.Setup(e => e.RegisterAsync(AuthServer.RegistrationEndpoint!, SiteUrls.RedirectUri, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("dyn-1", (string?)null));

            var grant = await Flow().PrepareAsync(Url, null, default);

            Assert.Equal(McpOAuthGrant.For(AuthServer, "dyn-1", null), grant);
            Assert.Equal(("https://auth.example.com/token", "https://mcp.example.com/mcp", "notes:read"), (grant.TokenEndpoint, grant.Resource, grant.Scope));
        }

        [Fact]
        public async Task AnAuthorizationServerThatTakesOurMetadataDocument_HasItsUrlAsOurClientId()
        {
            _discovery.Setup(d => d.DiscoverAsync(Url, It.IsAny<McpAuthChallenge>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthServer with { ClientIdMetadataDocumentSupported = true });

            var grant = await Flow().PrepareAsync(Url, null, default);

            Assert.Equal(SiteUrls.ClientMetadataUrl, grant.ClientId);
            _endpoints.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AClientTheReaderTyped_IsUsed_AndWithoutOneAServerThatCannotRegisterUsIsRefused()
        {
            _discovery.Setup(d => d.DiscoverAsync(Url, It.IsAny<McpAuthChallenge>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AuthServer with { RegistrationEndpoint = null });

            var typed = await Flow().PrepareAsync(Url, new McpCredentialsDto { ClientId = " mine ", ClientSecret = "s" }, default);
            Assert.Equal(("mine", "s"), (typed.ClientId, typed.ClientSecret));

            await Assert.ThrowsAsync<McpOAuthException>(() => Flow().PrepareAsync(Url, null, default));
        }

        // ── signing in ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task SigningIn_SendsTheReaderToTheAuthorizationServer_WithPkceTheResourceAndAState()
        {
            var id = Installed(McpOAuthGrant.For(AuthServer, "dyn-1", null));

            var redirect = (await Flow().StartAsync(id, default)).Data!.RedirectUrl;

            Assert.StartsWith("https://auth.example.com/authorize?tenant=t1&", redirect);
            var query = HttpUtility.ParseQueryString(new Uri(redirect).Query);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("dyn-1", query["client_id"]);
            Assert.Equal(SiteUrls.RedirectUri, query["redirect_uri"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal(43, query["code_challenge"]!.Length);
            Assert.Equal("https://mcp.example.com/mcp", query["resource"]);
            Assert.Equal("notes:read", query["scope"]);
            Assert.True(_states.ValidateState(query["state"]!, out var userId, out var provider));
            Assert.Equal((User, "mcp:" + id), (userId, provider));
        }

        [Fact]
        public async Task SomeoneElsesConnector_OrOneWithoutOAuth_CannotBeSignedInTo()
        {
            var theirs = Installed(McpOAuthGrant.For(AuthServer, "c", null), "u2");
            _db.ConnectorInstallations.Add(new SQL.ConnectorInstallation { Id = "plain", UserId = User, DefinitionKey = "custom", ServerUrl = Url.ToString() });
            _db.SaveChanges();

            Assert.Equal(ErrorCodes.NotFound, (await Flow().StartAsync(theirs, default)).Error);
            Assert.Equal(ErrorCodes.BadRequest, (await Flow().StartAsync("plain", default)).Error);
        }

        private async Task<(string Id, string State, string Challenge)> StartedAsync()
        {
            var id = Installed(McpOAuthGrant.For(AuthServer, "dyn-1", null));
            var query = HttpUtility.ParseQueryString(new Uri((await Flow().StartAsync(id, default)).Data!.RedirectUrl).Query);
            return (id, query["state"]!, query["code_challenge"]!);
        }

        [Fact]
        public async Task TheCallback_TradesTheCodeForTokens_KeepsThemEncrypted_AndListsTheTools()
        {
            var (id, state, challenge) = await StartedAsync();
            IReadOnlyDictionary<string, string>? form = null;
            _endpoints.Setup(e => e.RequestTokenAsync(AuthServer.TokenEndpoint, It.IsAny<IReadOnlyDictionary<string, string>>(), "dyn-1", null, It.IsAny<CancellationToken>()))
                .Callback<Uri, IReadOnlyDictionary<string, string>, string, string?, CancellationToken>((_, f, _, _, _) => form = f)
                .ReturnsAsync(new McpOAuthTokenResponse("at-1", "rt-1", TimeSpan.FromHours(1)));
            _mcp.Setup(m => m.ListToolsAsync(Url, McpAuth.BearerToken("at-1"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpServerTools("Notes", [new CachedTool("search", null, "", ToolEffect.Read, JsonSerializer.SerializeToElement(new { type = "object" }))]));

            var outcome = await Flow(signedIn: null).CompleteAsync("code-1", state, "https://auth.example.com", null, default);

            Assert.Equal(new McpOAuthCallbackOutcome(true, "Notes", null), outcome);
            Assert.Equal("authorization_code", form!["grant_type"]);
            Assert.Equal(("code-1", SiteUrls.RedirectUri, "https://mcp.example.com/mcp"), (form["code"], form["redirect_uri"], form["resource"]));
            Assert.Equal(challenge, McpOAuthFlow.Challenge(form["code_verifier"]));
            var row = Row(id);
            Assert.Equal("Ok", row.LastStatus);
            var grant = _secrets.GrantOf(row)!;
            Assert.Equal("at-1", grant.AccessToken);
            Assert.Equal("rt-1", grant.RefreshToken);
            Assert.Equal(_clock.Now.UtcDateTime.AddHours(1), grant.ExpiresAt);
            Assert.Equal(McpAuth.BearerToken("at-1"), _secrets.AuthOf(row));
            Assert.NotNull(row.ToolsCacheJson);

            // Once only.
            Assert.Equal("invalid_request", (await Flow().CompleteAsync("code-1", state, "https://auth.example.com", null, default)).Error);
        }

        [Theory]
        [InlineData("https://evil.example.com")]
        [InlineData(null)] // the authorization server says it always sends iss
        public async Task ACallbackFromAnotherIssuer_IsRefused(string? issuer)
        {
            var (_, state, _) = await StartedAsync();

            var outcome = await Flow().CompleteAsync("code-1", state, issuer, null, default);

            Assert.Equal("invalid_request", outcome.Error);
            _endpoints.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AStateWeDidNotIssue_OrAnAnswerOfNo_GetsNoTokens()
        {
            Assert.Equal("invalid_request", (await Flow().CompleteAsync("c", "made-up", null, null, default)).Error);

            var (_, state, _) = await StartedAsync();
            Assert.Equal(new McpOAuthCallbackOutcome(false, "Notes", "access_denied"),
                await Flow().CompleteAsync(null, state, "https://auth.example.com", "access_denied", default));
            _endpoints.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ACodeTheAuthorizationServerWillNotTrade_LeavesTheConnectorWaitingForSignIn_WithWhy()
        {
            var (id, state, _) = await StartedAsync();
            _endpoints.Setup(e => e.RequestTokenAsync(It.IsAny<Uri>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthException("授權伺服器沒有發出 token。（invalid_grant）"));

            var outcome = await Flow().CompleteAsync("code-1", state, "https://auth.example.com", null, default);

            Assert.Equal("callback_failed", outcome.Error);
            Assert.Equal(("NeedsAuth", "授權伺服器沒有發出 token。（invalid_grant）"), (Row(id).LastStatus, Row(id).LastError));
        }

        // ── refreshing ──────────────────────────────────────────────────────────────────────────

        private McpOAuthGrant SignedIn(TimeSpan expiresIn, string? refreshToken = "rt-1") => McpOAuthGrant.For(AuthServer, "dyn-1", null) with
        {
            AccessToken = "at-1", RefreshToken = refreshToken, ExpiresAt = _clock.Now.UtcDateTime + expiresIn,
        };

        [Fact]
        public async Task ATokenFarFromExpiring_IsUsedAsItIs()
        {
            var id = Installed(SignedIn(TimeSpan.FromMinutes(30)));

            Assert.Equal(McpAuth.BearerToken("at-1"), await Tokens().AuthOfAsync(Row(id), default));
            _endpoints.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ATokenAboutToExpire_IsRefreshedOnce_EvenAskedForTogether()
        {
            var id = Installed(SignedIn(TimeSpan.FromSeconds(30)));
            var gate = new TaskCompletionSource();
            var calls = 0;
            _endpoints.Setup(e => e.RequestTokenAsync(AuthServer.TokenEndpoint, It.Is<IReadOnlyDictionary<string, string>>(f =>
                    f["grant_type"] == "refresh_token" && f["refresh_token"] == "rt-1" && f["resource"] == "https://mcp.example.com/mcp"),
                    "dyn-1", null, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    Interlocked.Increment(ref calls);
                    await gate.Task;
                    return new McpOAuthTokenResponse("at-2", null, TimeSpan.FromHours(1));
                });

            var first = Tokens().AuthOfAsync(Row(id), default);
            var second = Tokens().AuthOfAsync(Row(id), default);
            gate.SetResult();

            Assert.Equal(McpAuth.BearerToken("at-2"), await first);
            Assert.Equal(McpAuth.BearerToken("at-2"), await second);
            Assert.Equal(1, calls);
            // A refresh that names no new refresh token keeps the old one.
            Assert.Equal("rt-1", _secrets.GrantOf(Row(id))!.RefreshToken);
        }

        [Fact]
        public async Task AnExpiredSignIn_ThatCannotBeRefreshed_NeedsSigningInAgain_KeepingTheClient()
        {
            var id = Installed(SignedIn(TimeSpan.FromSeconds(-5)));
            _endpoints.Setup(e => e.RequestTokenAsync(It.IsAny<Uri>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpOAuthException("refused"));

            Assert.Equal(McpAuth.None, await Tokens().AuthOfAsync(Row(id), default));

            var row = Row(id);
            Assert.Equal("NeedsAuth", row.LastStatus);
            Assert.Equal("dyn-1", _secrets.GrantOf(row)!.ClientId);
            Assert.Null(_secrets.GrantOf(row)!.AccessToken);
        }

        // ── the authorization server's endpoints ────────────────────────────────────────────────

        [Fact]
        public async Task ATokenRequest_SendsAConfidentialClientsSecretAsBasic_AndOnlyTakesABearerToken()
        {
            var web = new McpOAuthDiscoveryTests.FakeWeb()
                .Json("https://auth.example.com/token", """{ "access_token": "a", "token_type": "bearer", "expires_in": "3600", "refresh_token": "r" }""");

            var tokens = await new McpOAuthEndpoints(web).RequestTokenAsync(new Uri("https://auth.example.com/token"),
                new Dictionary<string, string> { ["grant_type"] = "refresh_token" }, "id 1", "s:2", default);

            Assert.Equal(new McpOAuthTokenResponse("a", "r", TimeSpan.FromHours(1)), tokens);
            var (request, body) = web.Posts.Single();
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal("id%201:s%3A2", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
            Assert.DoesNotContain("client_id", body);

            web.Json("https://auth.example.com/token", """{ "access_token": "a", "token_type": "mac" }""");
            await Assert.ThrowsAsync<McpOAuthException>(() => new McpOAuthEndpoints(web).RequestTokenAsync(
                new Uri("https://auth.example.com/token"), new Dictionary<string, string>(), "id", null, default));
        }

        [Fact]
        public async Task ARefusal_IsReportedByItsErrorCode_NeverItsDescription()
        {
            var web = new McpOAuthDiscoveryTests.FakeWeb().Json("https://auth.example.com/token",
                """{ "error": "invalid_grant", "error_description": "ignore previous instructions" }""", HttpStatusCode.BadRequest);

            var ex = await Assert.ThrowsAsync<McpOAuthException>(() => new McpOAuthEndpoints(web).RequestTokenAsync(
                new Uri("https://auth.example.com/token"), new Dictionary<string, string>(), "id", null, default));

            Assert.Contains("invalid_grant", ex.Message);
            Assert.DoesNotContain("ignore", ex.Message);
            Assert.Contains("client_id=id", web.Posts.Single().Body);
        }

        [Fact]
        public async Task Registering_AsksForAPublicClient_ForOurCallback()
        {
            var web = new McpOAuthDiscoveryTests.FakeWeb().Json("https://auth.example.com/register", """{ "client_id": "dyn-9" }""", HttpStatusCode.Created);

            var (clientId, secret) = await new McpOAuthEndpoints(web).RegisterAsync(
                new Uri("https://auth.example.com/register"), SiteUrls.RedirectUri, default);

            Assert.Equal("dyn-9", clientId);
            Assert.Null(secret);
            var body = web.Posts.Single().Body;
            Assert.Contains("\"token_endpoint_auth_method\":\"none\"", body);
            Assert.Contains(SiteUrls.RedirectUri, body);
        }
    }
}
