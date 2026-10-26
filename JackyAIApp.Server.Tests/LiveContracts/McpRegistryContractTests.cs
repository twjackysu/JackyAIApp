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
        public async Task LatestServers_IncludeSomeReachableOverStreamableHttp_AndPagesGoOnByCursor()
        {
            // Most of the registry is local (stdio) servers, so a few pages may pass before a remote one; the app walks them all.
            string? cursor = null;
            for (var page = 1; page <= 5; page++)
            {
                using var response = await Http.GetAsync(McpRegistry.UrlFor(cursor));
                Assert.True(response.IsSuccessStatusCode, $"MCP registry returned HTTP {(int)response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array,
                    "MCP registry response has no 'servers' array — McpRegistry.Parse reads this exact path.");
                Assert.True(servers.GetArrayLength() > 0, "MCP registry listed no servers on a page.");

                var parsed = McpRegistry.Parse(doc.RootElement);
                Assert.All(parsed.Servers, s => Assert.StartsWith("https://", s.Url));
                if (parsed.Servers.Count > 0) return;

                Assert.True(parsed.NextCursor != null,
                    "MCP registry page has no remote server and no 'metadata.nextCursor' — McpRegistry walks pages by it.");
                cursor = parsed.NextCursor;
            }
            Assert.Fail("No remote (Streamable HTTP) server in the registry's first 500 — a field McpRegistry reads may have moved.");
        }
    }
}
