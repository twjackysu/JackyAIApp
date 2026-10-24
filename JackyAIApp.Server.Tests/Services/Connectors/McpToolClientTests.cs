using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// The MCP client against a minimal Streamable HTTP server played by an HttpMessageHandler: the real SDK
    /// does the protocol, the test plays the server's side of it.
    /// </summary>
    public class McpToolClientTests
    {
        private static readonly Uri Server = new("https://mcp.example.com/mcp");

        /// <summary>A server with two tools that answers JSON (not SSE), optionally wanting a header.</summary>
        private sealed class FakeMcpServer : HttpMessageHandler
        {
            public (string Name, string Value)? RequiredHeader { get; init; }
            public List<string> Methods { get; } = [];
            public JsonObject? LastCallParams { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (RequiredHeader is var (name, value) &&
                    !(request.Headers.TryGetValues(name, out var sent) && sent.Contains(value)))
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (request.Method != HttpMethod.Post)
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

                var message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                var method = message["method"]?.GetValue<string>() ?? "";
                Methods.Add(method);
                if (message["id"] is not { } id) return new HttpResponseMessage(HttpStatusCode.Accepted);

                JsonNode result = method switch
                {
                    "initialize" => new JsonObject
                    {
                        ["protocolVersion"] = message["params"]!["protocolVersion"]!.GetValue<string>(),
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "fake", ["title"] = "Fake Notes", ["version"] = "1.0" },
                    },
                    "tools/list" => JsonNode.Parse("""
                        { "tools": [
                          { "name": "search_notes", "title": "搜尋筆記", "description": "Search the notes.",
                            "inputSchema": { "type": "object", "properties": { "q": { "type": "string" } } },
                            "annotations": { "readOnlyHint": true } },
                          { "name": "delete_note", "description": "Delete a note.", "inputSchema": { "type": "object" },
                            "annotations": { "destructiveHint": true } },
                          { "name": "add_note", "inputSchema": { "type": "object" } }
                        ] }
                        """)!,
                    "tools/call" => CallResult(message["params"]!.AsObject()),
                    _ => new JsonObject(),
                };
                var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(reply.ToJsonString(), Encoding.UTF8, "application/json"),
                };
            }

            private JsonNode CallResult(JsonObject call)
            {
                LastCallParams = (JsonObject)call.DeepClone();
                var failed = call["name"]!.GetValue<string>() == "delete_note";
                return new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = failed ? "not allowed" : "找到 2 則筆記" }),
                    ["isError"] = failed,
                };
            }
        }

        private sealed class Handlers(HttpMessageHandler handler) : IMcpHttpHandlerSource
        {
            public HttpMessageHandler Create() => new NotDisposed(handler);

            // The client disposes its handler; the test keeps looking at the server's.
            private sealed class NotDisposed(HttpMessageHandler inner) : DelegatingHandler(inner)
            {
                protected override void Dispose(bool disposing) { }
            }
        }

        private static McpToolClient Client(FakeMcpServer server) => new(new Handlers(server), NullLoggerFactory.Instance);

        [Fact]
        public async Task TheServersTools_AreListed_WithWhatTheyDo()
        {
            var server = new FakeMcpServer();

            var listed = await Client(server).ListToolsAsync(Server, McpAuth.None, CancellationToken.None);

            Assert.Equal("Fake Notes", listed.ServerName);
            Assert.Equal(new[] { ("search_notes", ToolEffect.Read), ("delete_note", ToolEffect.Destructive), ("add_note", ToolEffect.Write) },
                listed.Tools.Select(t => (t.Name, t.Effect)));
            Assert.Equal("搜尋筆記", listed.Tools[0].Title);
            Assert.Equal("string", listed.Tools[0].InputSchema.GetProperty("properties").GetProperty("q").GetProperty("type").GetString());
            Assert.Equal("initialize", server.Methods[0]);
            Assert.Contains("tools/list", server.Methods);
        }

        [Fact]
        public async Task ACall_SendsItsArguments_AndComesBackAsText()
        {
            var server = new FakeMcpServer();

            var outcome = await Client(server).CallToolAsync(Server, McpAuth.None, "search_notes",
                JsonSerializer.SerializeToElement(new { q = "會議" }), CancellationToken.None);

            Assert.Equal(new McpToolOutcome("找到 2 則筆記", false), outcome);
            Assert.Equal("會議", server.LastCallParams!["arguments"]!["q"]!.GetValue<string>());
        }

        [Fact]
        public async Task AToolsOwnFailure_IsAnOutcome_NotAnException()
        {
            var outcome = await Client(new FakeMcpServer()).CallToolAsync(Server, McpAuth.None, "delete_note",
                JsonSerializer.SerializeToElement(new { }), CancellationToken.None);

            Assert.True(outcome.IsError);
            Assert.Equal("not allowed", outcome.Text);
        }

        [Fact]
        public async Task TheCredentials_GoWithEveryRequest()
        {
            var server = new FakeMcpServer { RequiredHeader = ("X-API-Key", "k1") };

            var listed = await Client(server).ListToolsAsync(Server, new McpAuth("X-API-Key", "k1"), CancellationToken.None);

            Assert.Equal(3, listed.Tools.Count);
        }

        [Fact]
        public async Task AServerThatWantsCredentials_IsNeedsAuth()
        {
            var server = new FakeMcpServer { RequiredHeader = ("Authorization", "Bearer t") };

            var ex = await Assert.ThrowsAsync<McpServerException>(() =>
                Client(server).ListToolsAsync(Server, McpAuth.None, CancellationToken.None));

            Assert.Equal(McpFailure.NeedsAuth, ex.Failure);
        }

        [Fact]
        public void AnAddressThatIsNotPublic_IsBlocked_InTheReadersWords()
        {
            var ex = McpToolClient.Classify(new HttpRequestException("x", new BlockedAddressException("10.0.0.1")), timedOut: false);

            Assert.Equal(McpFailure.Blocked, ex.Failure);
            Assert.DoesNotContain("10.0.0.1", ex.Message);
        }

        [Fact]
        public void AnyOtherFailure_IsUnreachable_WithoutTheServersOwnText()
        {
            var ex = McpToolClient.Classify(new InvalidOperationException("secret internals"), timedOut: false);

            Assert.Equal(McpFailure.Unreachable, ex.Failure);
            Assert.DoesNotContain("secret", ex.Message);
        }

        [Fact]
        public void AResultWithoutText_IsDescribed()
        {
            var result = new CallToolResult
            {
                Content = [new TextContentBlock { Text = "a" }, ImageContentBlock.FromBytes(new byte[] { 1 }, "image/png")],
            };

            Assert.Equal("a\n[image]", McpToolClient.TextOf(result));
        }
    }
}
