using System.Text.Json;
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
    /// A whole MCP sign-in with nothing mocked but the web (<see cref="FakeAuthorizationServer"/>) and the
    /// reader's browser (<see cref="FakeAuthorizationServer.Approve"/>): the real discovery, flow, token
    /// endpoints, MCP client and the chat's connector tools, from the page's probe to a token refreshed for
    /// a tool call. The template for a connector of a new kind of sign-in (docs/ai-native/CONTRIBUTING-CONNECTORS.md).
    /// </summary>
    public class McpOAuthSignInTests : IDisposable
    {
        private const string User = "u1";
        private static readonly string Url = FakeAuthorizationServer.Server.ToString();
        private const string RedirectUri = "https://apps.example.com/api/connectors/callback/mcp";

        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        // One context a request, as the app has: a context kept across them would answer from rows it already tracks.
        private readonly List<AzureSQLDBContext> _requests = [];
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly TestStateService _states = new();
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero));
        private readonly ConnectorSecrets _secrets;
        private readonly ICurrentUser _user;
        private FakeAuthorizationServer _web = new();

        public McpOAuthSignInTests()
        {
            var encryption = new Mock<ITokenEncryptionService>();
            encryption.Setup(e => e.EncryptToken(It.IsAny<string>())).Returns<string>(s => "enc:" + s);
            encryption.Setup(e => e.DecryptToken(It.IsAny<string>())).Returns<string>(s => s["enc:".Length..]);
            _secrets = new ConnectorSecrets(encryption.Object, NullLogger<ConnectorSecrets>.Instance);
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(User);
            _user = user.Object;
        }

        public void Dispose()
        {
            foreach (var db in _requests) db.Dispose();
            _cache.Dispose();
        }

        // ── the services as the app wires them, every outbound request to the fake web ─────────────

        private AzureSQLDBContext Request()
        {
            var db = new AzureSQLDBContext(_options);
            _requests.Add(db);
            return db;
        }

        private McpToolClient Mcp() => new(_web, NullLoggerFactory.Instance);

        private McpOAuthTokens Tokens() =>
            new(new TestDbContextFactory(_options), _secrets, new McpOAuthEndpoints(_web), _clock, NullLogger<McpOAuthTokens>.Instance);

        private McpOAuthFlow Flow() => new(Request(), _user, _states, _cache, new McpOAuthDiscovery(_web), new McpOAuthEndpoints(_web), Mcp(),
            _secrets, UrlSource(), _clock, NullLogger<McpOAuthFlow>.Instance);

        private ConnectorInstallService Install() =>
            new(Request(), _user, Mcp(), _secrets, new McpOAuthDiscovery(_web), Flow(), Tokens(), UrlSource(), _clock);

        private static McpOAuthUrlSource UrlSource() => new(Microsoft.Extensions.Options.Options.Create(
            new JackyAIApp.Server.Configuration.ConnectorOptions { CallbackBaseUrl = "https://apps.example.com/api/connectors/callback" }));

        private SQL.ConnectorInstallation Row(string id)
        {
            using var db = new AzureSQLDBContext(_options);
            return db.ConnectorInstallations.AsNoTracking().Single(c => c.Id == id);
        }

        /// <summary>What the page does: add the server with an OAuth sign-in, send the reader there, and take them back.</summary>
        private async Task<string> SignedInAsync(McpCredentialsDto? typedClient = null)
        {
            var created = await Install().CreateAsync(new CreateInstallationRequestDto
            {
                DefinitionKey = SQL.ConnectorInstallation.CustomKey, ServerUrl = Url, AuthKind = "oauth", Credentials = typedClient,
            }, default);
            Assert.True(created.IsSuccess, created.ErrorMessage);
            var id = created.Data!.Id!;
            Assert.Equal("NeedsAuth", Row(id).LastStatus);

            await SignInAgainAsync(id);
            return id;
        }

        private async Task SignInAgainAsync(string id)
        {
            var start = await Flow().StartAsync(id, default);
            Assert.True(start.IsSuccess, start.ErrorMessage);
            var back = _web.Approve(start.Data!.RedirectUrl);
            var outcome = await Flow().CompleteAsync(back.Code, back.State, back.Iss, null, default);
            Assert.True(outcome.Ok, outcome.Error);
        }

        /// <summary>The chat's tool for one of the connector's tools, read from the database as a turn reads it.</summary>
        private async Task<IAiTool> ChatToolAsync(string toolName)
        {
            await using var db = new AzureSQLDBContext(_options);
            var tools = await new ConnectorToolSource(db, Mcp(), Tokens(), NullLogger<ConnectorToolSource>.Instance).ForUserAsync(User, default);
            return tools.Single(t => t.Descriptor.Name == ToolNames.ForModel("m1", toolName));
        }

        private static Task<AiToolResult> Run(IAiTool tool) =>
            tool.RunAsync(JsonSerializer.SerializeToElement(new { q = "會議" }), new AiToolContext(User, "c1"), default);

        // ── the sign-in ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task TheProbe_FindsTheSignIn_AndThatWeCanRegister()
        {
            var probe = await Install().ProbeAsync(Url, default);

            Assert.True(probe.IsSuccess, probe.ErrorMessage);
            Assert.Equal(new McpProbeResultDto(Url, "oauth", "auth.example.com", "dcr", null), probe.Data);
            Assert.Empty(_web.Clients);
        }

        [Fact]
        public async Task AReader_SignsIn_AndTheChatUsesTheServersTools_RefreshingTheTokenWhenItIsDue()
        {
            var id = await SignedInAsync();

            // Registered once, for our callback; the code was taken for tokens and the tools listed with them.
            Assert.Single(_web.Clients);
            Assert.Equal(new[] { "authorization_code" }, _web.TokensGranted);
            var row = Row(id);
            Assert.Equal("Ok", row.LastStatus);
            Assert.Equal(new[] { "search_notes", "delete_note", "add_note" }, CachedTools.Parse(row.ToolsCacheJson).Select(t => t.Name));
            var grant = _secrets.GrantOf(row)!;
            Assert.Equal(_clock.Now.UtcDateTime.AddHours(1), grant.ExpiresAt);
            Assert.NotNull(grant.RefreshToken);

            Assert.Equal(new AiToolResult("找到 2 則筆記"), await Run(await ChatToolAsync("search_notes")));
            Assert.Equal($"Bearer {grant.AccessToken}", _web.McpServer.Authorizations[^1]);

            // An hour later the token is refreshed before the call (the old one no longer works on the server).
            _clock.Now = _clock.Now.AddHours(1);
            Assert.Equal(new AiToolResult("找到 2 則筆記"), await Run(await ChatToolAsync("search_notes")));
            Assert.Equal(new[] { "authorization_code", "refresh_token" }, _web.TokensGranted);
            var refreshed = _secrets.GrantOf(Row(id))!;
            Assert.NotEqual(grant.AccessToken, refreshed.AccessToken);
            Assert.NotEqual(grant.RefreshToken, refreshed.RefreshToken);
            Assert.Equal($"Bearer {refreshed.AccessToken}", _web.McpServer.Authorizations[^1]);
        }

        [Fact]
        public async Task TestingTheConnection_UsesItsSignIn()
        {
            var id = await SignedInAsync();

            var test = await Install().TestAsync(id, default);

            Assert.True(test.IsSuccess, test.ErrorMessage);
            var result = test.Data!;
            Assert.True(result.Ok, result.Error);
            Assert.Equal(3, result.Connector.Tools.Count);
        }

        [Fact]
        public async Task AnAuthorizationServerThatCannotRegisterUs_TakesTheClientTheReaderMadeThere()
        {
            _web = new FakeAuthorizationServer { Registration = false };
            _web.RegisterClient("my-app", RedirectUri);
            var probe = (await Install().ProbeAsync(Url, default)).Data!;
            Assert.Equal("manual", probe.Registration);
            // What the reader registers there, so the server sends them back to us.
            Assert.Equal(RedirectUri, probe.RedirectUri);

            var id = await SignedInAsync(new McpCredentialsDto { ClientId = "my-app" });

            Assert.Equal("my-app", _secrets.GrantOf(Row(id))!.ClientId);
            Assert.Equal("Ok", Row(id).LastStatus);
        }

        [Fact]
        public async Task ASignInTakenAwayOnTheServer_NeedsSigningInAgain_WithTheSameClient()
        {
            var id = await SignedInAsync();
            _web.RevokeAll();

            // Before the token is due: the server refuses it, and the reader is told to sign in again.
            var refused = await Run(await ChatToolAsync("search_notes"));
            Assert.True(refused.IsError);
            Assert.Equal("NeedsAuth", Row(id).LastStatus);

            // Once it is due, the refresh is refused too, and the tokens are dropped.
            _clock.Now = _clock.Now.AddHours(1);
            Assert.Equal(McpAuth.None, await Tokens().AuthOfAsync(Row(id), default));
            Assert.Null(_secrets.GrantOf(Row(id))!.AccessToken);

            // Signing in again needs no new registration.
            await SignInAgainAsync(id);
            Assert.Single(_web.Clients);
            Assert.Equal("Ok", Row(id).LastStatus);
            Assert.Equal(new AiToolResult("找到 2 則筆記"), await Run(await ChatToolAsync("search_notes")));
        }

        [Fact]
        public async Task ACallThatNeedsMoreScope_AsksForItWhenTheReaderSignsInAgain()
        {
            var id = await SignedInAsync();
            Assert.Equal(FakeAuthorizationServer.Scope, _web.ScopesAsked.Single());
            _web.McpServer.CallsNeedScope = "notes:write";

            var refused = await Run(await ChatToolAsync("add_note"));

            Assert.True(refused.IsError);
            Assert.Contains("更多權限", refused.Content);
            var row = Row(id);
            Assert.Equal("NeedsAuth", row.LastStatus);
            var grant = _secrets.GrantOf(row)!;
            Assert.Equal("notes:read notes:write", grant.Scope);
            // The token stays for what it is enough for.
            Assert.NotNull(grant.AccessToken);

            _web.McpServer.CallsNeedScope = null;
            await SignInAgainAsync(id);
            Assert.Equal("notes:read notes:write", _web.ScopesAsked[^1]);
            Assert.Equal(new AiToolResult("找到 2 則筆記"), await Run(await ChatToolAsync("add_note")));
        }

        [Fact]
        public async Task ACodeComesBackOnce()
        {
            var created = await Install().CreateAsync(new CreateInstallationRequestDto
            {
                DefinitionKey = SQL.ConnectorInstallation.CustomKey, ServerUrl = Url, AuthKind = "oauth",
            }, default);
            var start = await Flow().StartAsync(created.Data!.Id!, default);
            var back = _web.Approve(start.Data!.RedirectUrl);

            Assert.True((await Flow().CompleteAsync(back.Code, back.State, back.Iss, null, default)).Ok);
            Assert.Equal("invalid_request", (await Flow().CompleteAsync(back.Code, back.State, back.Iss, null, default)).Error);
            Assert.Equal(new[] { "authorization_code" }, _web.TokensGranted);
        }
    }
}
