using JackyAIApp.Server.Services.Connectors.SiteMcp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.Services.Connectors.SiteMcp
{
    /// <summary>The site as an OAuth authorization server for its MCP server: redirect URIs, clients without a table, codes, PKCE.</summary>
    public class SiteOAuthTests
    {
        [Theory]
        [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
        [InlineData("https://chatgpt.com/connector_platform_oauth_redirect", true)]
        [InlineData("http://localhost:33418/callback", true)]
        [InlineData("http://127.0.0.1:6274/oauth/callback", true)]
        [InlineData("http://example.com/callback", false)]
        [InlineData("https://claude.ai/cb#x", false)]
        [InlineData("https://user:pw@claude.ai/cb", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("/relative", false)]
        [InlineData("", false)]
        public void RedirectUris_AreHttps_OrThisMachine(string uri, bool allowed)
        {
            Assert.Equal(allowed, SiteOAuthRedirects.IsAllowed(uri));
        }

        private static SiteOAuthClients Clients(IDataProtectionProvider? keys = null) => new(keys ?? new EphemeralDataProtectionProvider());

        [Fact]
        public void AClientId_IsItsRegistration_ReadBack()
        {
            var clients = Clients();

            var id = clients.Register("Claude", ["https://claude.ai/api/mcp/auth_callback", "https://claude.ai/api/mcp/auth_callback"]);

            var client = clients.Read(id);
            Assert.NotNull(client);
            Assert.Equal("Claude", client.Name);
            Assert.Equal(new[] { "https://claude.ai/api/mcp/auth_callback" }, client.RedirectUris);
        }

        [Fact]
        public void AClientIdMadeUp_OrChanged_OrUnderOtherKeys_IsUnknown()
        {
            var clients = Clients();
            var id = clients.Register("Claude", ["https://claude.ai/cb"])!;

            Assert.Null(clients.Read("made-up"));
            Assert.Null(clients.Read(id[..^2] + (id[^2] == 'A' ? "BB" : "AA")));
            Assert.Null(clients.Read(""));
            Assert.Null(Clients().Read(id));
        }

        [Fact]
        public void ARegistration_WithARedirectNotAllowed_OrNone_IsRefused()
        {
            var clients = Clients();

            Assert.Null(clients.Register("x", ["https://claude.ai/cb", "http://evil.example/cb"]));
            Assert.Null(clients.Register("x", []));
            Assert.Null(clients.Register("x", null));
            Assert.Null(clients.Register("x", Enumerable.Range(0, 11).Select(i => $"https://a.example/{i}").ToList()));
        }

        [Theory]
        [InlineData(null, "AI")]
        [InlineData("   ", "AI")]
        [InlineData(" Claude\u0007 ", "Claude")]
        [InlineData("A client whose name goes on and on and on forever", "A client whose name goes on and on and o")]
        public void AClientsName_IsCleanedAndCut(string? name, string expected)
        {
            Assert.Equal(expected, SiteOAuthClients.NameOf(name));
        }

        private static SiteOAuthGrant Grant() =>
            new("u1", "client", "Claude", "https://claude.ai/cb", "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", AllowWrites: false);

        [Fact]
        public void ACode_IsGoodOnce()
        {
            var codes = new SiteOAuthCodes(new MemoryCache(new MemoryCacheOptions()));
            var code = codes.Issue(Grant());

            Assert.Equal(Grant(), codes.Redeem(code));
            Assert.Null(codes.Redeem(code));
            Assert.Null(codes.Redeem("other"));
            Assert.Null(codes.Redeem(null));
        }

        [Fact]
        public void Pkce_ChecksTheVerifierAgainstItsS256Challenge()
        {
            // RFC 7636 appendix B.
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            const string challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

            Assert.True(Pkce.IsS256Challenge(challenge));
            Assert.True(Pkce.Verifies(verifier, challenge));
            Assert.False(Pkce.Verifies(verifier[..^1] + "l", challenge));
            Assert.False(Pkce.Verifies("short", challenge));
            Assert.False(Pkce.Verifies(null, challenge));
            Assert.False(Pkce.IsS256Challenge("plain-challenge"));
        }
    }
}
