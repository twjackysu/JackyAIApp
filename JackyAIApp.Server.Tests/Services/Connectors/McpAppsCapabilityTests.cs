using System.Text;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// The chat says in each <c>initialize</c> that it draws MCP Apps (<see cref="McpAppsCapability"/>, D-203), so a server
    /// that offers its app tools only to such a client (the extension's own server SDK, <c>getUiCapability</c>) offers them.
    /// </summary>
    public class McpAppsCapabilityTests
    {
        private static readonly Uri Server = new("https://mcp.example.com/mcp");

        private const string ToolsWithApp = """
            { "tools": [
              { "name": "forecast", "inputSchema": { "type": "object" }, "annotations": { "readOnlyHint": true },
                "_meta": { "ui": { "resourceUri": "ui://weather/forecast.html" } } }
            ] }
            """;

        [Fact]
        public async Task TheInitialize_SaysTheChatDrawsApps_AndTheProtocolStillWorks()
        {
            var server = new FakeMcpServer();

            var listed = await new McpToolClient(server, NullLoggerFactory.Instance).ListToolsAsync(Server, McpAuth.None, CancellationToken.None);

            var extension = server.InitializeParams!["capabilities"]!["extensions"]![McpAppsCapability.ExtensionId]!;
            Assert.Equal(new[] { McpToolClient.AppMimeType }, extension["mimeTypes"]!.AsArray().Select(t => t!.GetValue<string>()));
            Assert.Equal("jacky-studio", server.InitializeParams["clientInfo"]!["name"]!.GetValue<string>());
            Assert.Equal(3, listed.Tools.Count);
        }

        [Fact]
        public async Task AServerThatOffersAppsOnlyToClientsThatDrawThem_OffersOursTheApp()
        {
            var server = new FakeMcpServer { ToolsJson = ToolsWithApp, AppsOnlyForUiClients = true };

            var listed = await new McpToolClient(server, NullLoggerFactory.Instance).ListToolsAsync(Server, McpAuth.None, CancellationToken.None);

            Assert.Equal("ui://weather/forecast.html", listed.Tools[0].UiResourceUri);
        }

        [Fact]
        public async Task TheFake_LeavesTheAppOut_ForAClientThatDidNotSaySo()
        {
            // The fake is what the test above leans on: without the extension it must hide the app.
            var plain = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"x","version":"1"}}}""";
            var server = new FakeMcpServer { ToolsJson = ToolsWithApp, AppsOnlyForUiClients = true };

            await SendAsync(server, plain);
            var listed = await SendAsync(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");

            Assert.Null(listed["result"]!["tools"]![0]!["_meta"]!["ui"]);
        }

        [Fact]
        public void OtherMessages_AndBodiesThatAreNotJson_AreLeftAlone()
        {
            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""")));
            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")));
            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes("not json")));
            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes("""[{"method":"initialize","params":{}}]""")));
            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes("""{"method":"initialize","params":{"capabilities":[]}}""")));
        }

        [Fact]
        public void OtherExtensions_AreKept_AndOneThatAlreadyNamesApps_IsNotChanged()
        {
            var withOther = McpAppsCapability.Declare(Encoding.UTF8.GetBytes(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{"roots":{},"extensions":{"x.example/y":{}}}}}"""));
            var capabilities = JsonNode.Parse(withOther!)!["params"]!["capabilities"]!;
            Assert.NotNull(capabilities["roots"]);
            Assert.NotNull(capabilities["extensions"]!["x.example/y"]);
            Assert.NotNull(capabilities["extensions"]![McpAppsCapability.ExtensionId]);

            Assert.Null(McpAppsCapability.Declare(Encoding.UTF8.GetBytes(
                """{"method":"initialize","params":{"capabilities":{"extensions":{"io.modelcontextprotocol/ui":{"mimeTypes":[]}}}}}""")));
        }

        [Fact]
        public void AnInitializeWithoutCapabilities_GetsThem()
        {
            var declared = McpAppsCapability.Declare(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}"""));

            Assert.NotNull(JsonNode.Parse(declared!)!["params"]!["capabilities"]!["extensions"]![McpAppsCapability.ExtensionId]);
        }

        private static async Task<JsonObject> SendAsync(FakeMcpServer server, string body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Server)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            using var response = await server.AnswerAsync(request, CancellationToken.None);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        }
    }
}
