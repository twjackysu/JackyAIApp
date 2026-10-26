using System.Text.Json;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract test for the official MCP Registry (registry.modelcontextprotocol.io), which the connectors
    /// page searches for remote MCP servers to add (<see cref="McpRegistry"/>, D-122). It asks exactly what the app
    /// asks and reads it with the app's own parser, so a change to the response's shape shows as no server left.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class McpRegistryContractTests : LiveContractBase
    {
        [Fact]
        public async Task LatestServers_IncludeSomeReachableOverStreamableHttp()
        {
            using var response = await Http.GetAsync(McpRegistry.UrlFor(""));
            Assert.True(response.IsSuccessStatusCode, $"MCP registry returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array,
                "MCP registry response has no 'servers' array — McpRegistry.Parse reads this exact path.");
            Assert.True(servers.GetArrayLength() > 0, "MCP registry listed no servers at all.");

            // A page of 100 latest servers has always held remote ones; none left means a field the parser reads moved.
            var parsed = McpRegistry.Parse(doc.RootElement);
            Assert.NotEmpty(parsed.Servers);
            Assert.All(parsed.Servers, s => Assert.StartsWith("https://", s.Url));
        }
    }
}
