using System.Net;
using System.Net.Http.Headers;
using System.Text;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>Working out an MCP server's sign-in from its metadata, against servers played by an HttpMessageHandler.</summary>
    public class McpOAuthDiscoveryTests
    {
        private static readonly Uri Server = new("https://mcp.example.com/mcp");

        /// <summary>Answers each URL it was given; 404 for any other. Records what was asked.</summary>
        internal sealed class FakeWeb : HttpMessageHandler, IMcpHttpHandlerSource
        {
            private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
            public List<string> Asked { get; } = [];
            public List<(HttpRequestMessage Request, string Body)> Posts { get; } = [];

            public FakeWeb Json(string url, string json, HttpStatusCode status = HttpStatusCode.OK)
            {
                _routes[url] = _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                return this;
            }

            public FakeWeb Answer(string url, Func<HttpRequestMessage, HttpResponseMessage> answer)
            {
                _routes[url] = answer;
                return this;
            }

            public HttpMessageHandler Create() => new Shared(this);

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var url = request.RequestUri!.ToString();
                Asked.Add(url);
                if (request.Content != null) Posts.Add((request, await request.Content.ReadAsStringAsync(ct)));
                return _routes.TryGetValue(url, out var answer) ? answer(request) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            /// <summary>The client disposes its handler; the fake lives on for the test to read.</summary>
            private sealed class Shared(FakeWeb web) : DelegatingHandler(web)
            {
                protected override void Dispose(bool disposing) { }
            }
        }

        private const string Prm = """
            { "resource": "https://mcp.example.com/mcp", "authorization_servers": ["https://auth.example.com"],
              "scopes_supported": ["notes:read", "notes:write"] }
            """;

        private const string AsMetadata = """
            { "issuer": "https://auth.example.com", "authorization_endpoint": "https://auth.example.com/authorize",
              "token_endpoint": "https://auth.example.com/token", "registration_endpoint": "https://auth.example.com/register",
              "code_challenge_methods_supported": ["S256"], "client_id_metadata_document_supported": true,
              "authorization_response_iss_parameter_supported": true }
            """;

        private static HttpResponseMessage Unauthorized(string? wwwAuthenticate)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (wwwAuthenticate != null) response.Headers.TryAddWithoutValidation("WWW-Authenticate", wwwAuthenticate);
            return response;
        }

        [Fact]
        public async Task A401_NamesWhereTheMetadataIs_AndTheScope()
        {
            var web = new FakeWeb().Answer(Server.ToString(), _ => Unauthorized(
                """Bearer error="invalid_token", resource_metadata="https://mcp.example.com/.well-known/oauth-protected-resource/mcp", scope="a b" """));

            var challenge = await new McpOAuthDiscovery(web).ChallengeAsync(Server, default);

            Assert.Equal(new McpAuthChallenge(new Uri("https://mcp.example.com/.well-known/oauth-protected-resource/mcp"), "a b"), challenge);
            Assert.Contains("\"initialize\"", web.Posts.Single().Body);
        }

        [Fact]
        public async Task AServerThatDoesNotAnswer401_HasNoChallenge()
        {
            var web = new FakeWeb().Json(Server.ToString(), "{}");

            Assert.Null(await new McpOAuthDiscovery(web).ChallengeAsync(Server, default));
        }

        [Fact]
        public void AuthParams_AreReadQuotedOrBare()
        {
            var found = McpOAuthDiscovery.ParametersOf("""realm="x\"y", resource_metadata=https://a.example/m, scope="" """);

            Assert.Equal("x\"y", found["realm"]);
            Assert.Equal("https://a.example/m", found["resource_metadata"]);
            Assert.Equal("", found["scope"]);
        }

        [Fact]
        public void AChallengeOnlyPointsAtHttpsMetadata()
        {
            var headers = new HttpResponseMessage().Headers.WwwAuthenticate;
            headers.Add(new AuthenticationHeaderValue("Bearer", "resource_metadata=\"http://mcp.example.com/m\""));

            Assert.Equal(new McpAuthChallenge(null, null), McpOAuthDiscovery.ChallengeOf(headers));
        }

        [Fact]
        public void TheMetadataIsLookedFor_WhereTheSpecSays_InOrder()
        {
            Assert.Equal(
                new[] { "https://mcp.example.com/.well-known/oauth-protected-resource/mcp", "https://mcp.example.com/.well-known/oauth-protected-resource" },
                McpOAuthDiscovery.ProtectedResourceMetadataUrls(Server, new McpAuthChallenge(null, null)).Select(u => u.ToString()));
            Assert.Equal(
                new[] { "https://auth.example.com/.well-known/oauth-authorization-server/tenant1",
                 "https://auth.example.com/.well-known/openid-configuration/tenant1",
                 "https://auth.example.com/tenant1/.well-known/openid-configuration" },
                McpOAuthDiscovery.AuthorizationServerMetadataUrls(new Uri("https://auth.example.com/tenant1")).Select(u => u.ToString()));
            Assert.Equal(
                new[] { "https://auth.example.com/.well-known/oauth-authorization-server", "https://auth.example.com/.well-known/openid-configuration" },
                McpOAuthDiscovery.AuthorizationServerMetadataUrls(new Uri("https://auth.example.com")).Select(u => u.ToString()));
        }

        [Fact]
        public async Task TheMetadata_LeadsToTheAuthorizationServer_AndItsEndpoints()
        {
            var web = new FakeWeb()
                .Json("https://mcp.example.com/.well-known/oauth-protected-resource/mcp", Prm)
                .Json("https://auth.example.com/.well-known/oauth-authorization-server", AsMetadata);

            var found = await new McpOAuthDiscovery(web).DiscoverAsync(Server, new McpAuthChallenge(null, null), default);

            Assert.Equal(new McpOAuthServer("https://mcp.example.com/mcp", "https://auth.example.com",
                new Uri("https://auth.example.com/authorize"), new Uri("https://auth.example.com/token"), new Uri("https://auth.example.com/register"),
                ClientIdMetadataDocumentSupported: true, IssParameterSupported: true, Scope: "notes:read notes:write"), found);
        }

        [Fact]
        public async Task TheChallengesScope_WinsOverTheScopesTheServerLists_AndOpenIdDiscoveryIsTheFallback()
        {
            var web = new FakeWeb()
                .Json("https://mcp.example.com/m.json", Prm)
                .Json("https://auth.example.com/.well-known/openid-configuration", AsMetadata);

            var found = await new McpOAuthDiscovery(web).DiscoverAsync(
                Server, new McpAuthChallenge(new Uri("https://mcp.example.com/m.json"), "notes:read"), default);

            Assert.Equal("notes:read", found.Scope);
            Assert.Equal("https://mcp.example.com/m.json", web.Asked[0]);
        }

        [Theory]
        // Metadata about another server.
        [InlineData("""{ "resource": "https://other.example.com/mcp", "authorization_servers": ["https://auth.example.com"] }""", AsMetadata)]
        // No authorization server, or one that is not https.
        [InlineData("""{ "resource": "https://mcp.example.com/mcp" }""", AsMetadata)]
        [InlineData("""{ "resource": "https://mcp.example.com/mcp", "authorization_servers": ["http://auth.example.com"] }""", AsMetadata)]
        // An authorization server whose metadata names another issuer, or that has no PKCE S256.
        [InlineData(Prm, """{ "issuer": "https://evil.example.com", "authorization_endpoint": "https://auth.example.com/a", "token_endpoint": "https://auth.example.com/t", "code_challenge_methods_supported": ["S256"] }""")]
        [InlineData(Prm, """{ "issuer": "https://auth.example.com", "authorization_endpoint": "https://auth.example.com/a", "token_endpoint": "https://auth.example.com/t", "code_challenge_methods_supported": ["plain"] }""")]
        [InlineData(Prm, """{ "issuer": "https://auth.example.com", "authorization_endpoint": "http://auth.example.com/a", "token_endpoint": "https://auth.example.com/t", "code_challenge_methods_supported": ["S256"] }""")]
        public async Task MetadataThatCannotBeTrusted_IsRefused(string prm, string asMetadata)
        {
            var web = new FakeWeb()
                .Json("https://mcp.example.com/.well-known/oauth-protected-resource/mcp", prm)
                .Json("https://auth.example.com/.well-known/oauth-authorization-server", asMetadata);

            await Assert.ThrowsAsync<McpOAuthException>(() =>
                new McpOAuthDiscovery(web).DiscoverAsync(Server, new McpAuthChallenge(null, null), default));
        }

        [Theory]
        [InlineData("https://mcp.example.com/mcp", true)]
        [InlineData("https://MCP.example.com/mcp/", true)]
        [InlineData("https://mcp.example.com", true)]
        [InlineData("https://mcp.example.com/", true)]
        [InlineData("https://mcp.example.com/mc", false)]
        [InlineData("https://mcp.example.com/other", false)]
        [InlineData("https://mcp.example.com:8443/mcp", false)]
        [InlineData("http://mcp.example.com/mcp", false)]
        [InlineData("https://evil.example.com/mcp", false)]
        [InlineData("https://mcp.example.com/mcp?x=1", false)]
        public void AResource_CoversTheServer_OnlyAtItsOriginAndAPathItIsWithin(string resource, bool covers)
        {
            Assert.Equal(covers, McpOAuthDiscovery.Covers(resource, Server));
        }

        [Fact]
        public async Task NoMetadataAnywhere_IsRefused_InWords()
        {
            var ex = await Assert.ThrowsAsync<McpOAuthException>(() =>
                new McpOAuthDiscovery(new FakeWeb()).DiscoverAsync(Server, new McpAuthChallenge(null, null), default));

            Assert.Contains("OAuth", ex.Message);
        }

        [Fact]
        public async Task ADocumentTooLargeToRead_IsNoDocument()
        {
            var web = new FakeWeb().Json("https://mcp.example.com/.well-known/oauth-protected-resource/mcp",
                $$"""{ "resource": "https://mcp.example.com/mcp", "pad": "{{new string('x', McpOAuthDiscovery.MaxDocumentBytes)}}" }""");

            await Assert.ThrowsAsync<McpOAuthException>(() =>
                new McpOAuthDiscovery(web).DiscoverAsync(Server, new McpAuthChallenge(null, null), default));
        }

        [Theory]
        [InlineData("https://apps.jacky-studio.com/api/connectors/callback", "https://apps.jacky-studio.com/api/connectors/oauth/client-metadata")]
        [InlineData("https://localhost:7034/api/connectors/callback/", null)]
        public void TheSitesAddresses_ComeFromTheCallbackBase(string callbackBase, string? metadata)
        {
            var urls = McpOAuthUrls.From(new ConnectorOptions { CallbackBaseUrl = callbackBase });

            Assert.Equal(callbackBase.TrimEnd('/') + "/mcp", urls.RedirectUri);
            Assert.Equal(metadata, urls.ClientMetadataUrl);
        }

        [Fact]
        public void HowWeBecomeAClient_PrefersOurMetadataDocument_ThenRegistering()
        {
            var server = new McpOAuthServer("r", "https://a", new Uri("https://a/x"), new Uri("https://a/t"), new Uri("https://a/r"), true, false, null);
            var site = new McpOAuthUrls("https://s/cb/mcp", "https://s/meta");

            Assert.Equal("cimd", site.RegistrationFor(server));
            Assert.Equal("dcr", (site with { ClientMetadataUrl = null }).RegistrationFor(server));
            Assert.Equal("dcr", site.RegistrationFor(server with { ClientIdMetadataDocumentSupported = false }));
            Assert.Equal("manual", site.RegistrationFor(server with { ClientIdMetadataDocumentSupported = false, RegistrationEndpoint = null }));
        }

        [Fact]
        public void OurMetadataDocument_IsAPublicClient_AtItsOwnUrl()
        {
            var document = McpClientMetadata.Document(new McpOAuthUrls("https://s.example/api/connectors/callback/mcp", "https://s.example/meta"));

            Assert.Equal("https://s.example/meta", document["client_id"]);
            Assert.Equal("none", document["token_endpoint_auth_method"]);
            Assert.Equal(new[] { "https://s.example/api/connectors/callback/mcp" }, (string[])document["redirect_uris"]);
            Assert.Equal("https://s.example", document["client_uri"]);
        }
    }
}
