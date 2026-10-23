using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// An MCP server's URL is kept in the canonical form the MCP authorization spec identifies the
    /// server by (RFC 8707's resource), so the same server typed two ways is one server.
    /// </summary>
    public class McpServerUrlTests
    {
        [Theory]
        [InlineData("https://mcp.example.com/mcp", "https://mcp.example.com/mcp")]
        [InlineData("  https://mcp.example.com/mcp  ", "https://mcp.example.com/mcp")]
        [InlineData("HTTPS://MCP.Example.COM/mcp", "https://mcp.example.com/mcp")]
        [InlineData("https://mcp.example.com/mcp/", "https://mcp.example.com/mcp")]
        [InlineData("https://mcp.example.com/", "https://mcp.example.com")]
        [InlineData("https://mcp.example.com", "https://mcp.example.com")]
        [InlineData("https://mcp.example.com:443/mcp", "https://mcp.example.com/mcp")]
        [InlineData("https://mcp.example.com:8443/mcp", "https://mcp.example.com:8443/mcp")]
        [InlineData("https://mcp.example.com/mcp#section", "https://mcp.example.com/mcp")]
        [InlineData("https://mcp.example.com/server/mcp?tenant=a", "https://mcp.example.com/server/mcp?tenant=a")]
        public void AUsableUrl_IsKeptInItsCanonicalForm(string input, string expected)
        {
            var (url, error) = McpServerUrl.Normalize(input);

            Assert.Null(error);
            Assert.Equal(expected, url!.OriginalString);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("mcp.example.com/mcp")]            // no scheme
        [InlineData("http://mcp.example.com/mcp")]     // not https
        [InlineData("ftp://mcp.example.com/mcp")]
        [InlineData("file:///etc/passwd")]
        [InlineData("https://user:pass@mcp.example.com/mcp")]
        [InlineData("not a url")]
        public void AnUnusableUrl_IsRefusedWithAReason(string? input)
        {
            var (url, error) = McpServerUrl.Normalize(input);

            Assert.Null(url);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }

        [Fact]
        public void ATooLongUrl_IsRefused()
        {
            var (url, error) = McpServerUrl.Normalize("https://mcp.example.com/" + new string('a', McpServerUrl.MaxLength));

            Assert.Null(url);
            Assert.Contains("太長", error);
        }
    }
}
