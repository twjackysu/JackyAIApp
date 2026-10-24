using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>
    /// A minimal MCP server over Streamable HTTP, answering JSON (not SSE), as the handler of the connectors'
    /// outbound requests: the real SDK (<see cref="McpToolClient"/>) does the protocol, this plays the server's
    /// side of it. It has three notes tools (a read, a destructive one that fails, a write with no hints), or
    /// the tools a test gives it, and may want credentials: <see cref="RequiredHeader"/> for a key, or
    /// <see cref="Accepts"/> for anything else (<see cref="FakeAuthorizationServer"/> checks its tokens there).
    /// A request without them is answered 401, with <see cref="Challenge"/> as its <c>WWW-Authenticate</c>.
    /// </summary>
    public sealed class FakeMcpServer : HttpMessageHandler, IMcpHttpHandlerSource
    {
        public const string DefaultTools = """
            { "tools": [
              { "name": "search_notes", "title": "搜尋筆記", "description": "Search the notes.",
                "inputSchema": { "type": "object", "properties": { "q": { "type": "string" } } },
                "annotations": { "readOnlyHint": true } },
              { "name": "delete_note", "description": "Delete a note.", "inputSchema": { "type": "object" },
                "annotations": { "destructiveHint": true } },
              { "name": "add_note", "inputSchema": { "type": "object" } }
            ] }
            """;

        /// <summary>The server's name in its <c>initialize</c> answer (<c>serverInfo.title</c>).</summary>
        public string Title { get; init; } = "Fake Notes";

        /// <summary>The <c>tools/list</c> result, as JSON.</summary>
        public string ToolsJson { get; init; } = DefaultTools;

        /// <summary>A header every request must carry with this value.</summary>
        public (string Name, string Value)? RequiredHeader { get; init; }

        /// <summary>Whether a request's credentials are good; any request is, without it (and <see cref="RequiredHeader"/>).</summary>
        public Func<HttpRequestMessage, bool>? Accepts { get; init; }

        /// <summary>The <c>WWW-Authenticate</c> of a 401 (MCP authorization: where the sign-in is described).</summary>
        public string? Challenge { get; init; }

        /// <summary>
        /// A scope every <c>tools/call</c> needs and the credentials lack: such a call is answered 403
        /// <c>insufficient_scope</c> naming it (MCP authorization's step-up). None by default.
        /// </summary>
        public string? CallsNeedScope { get; set; }

        /// <summary>The JSON-RPC method of every request with credentials it took, in order.</summary>
        public List<string> Methods { get; } = [];

        /// <summary>The <c>Authorization</c> header of every request it took (null for none).</summary>
        public List<string?> Authorizations { get; } = [];

        public JsonObject? LastCallParams { get; private set; }

        public HttpMessageHandler Create() => new KeptAlive(this);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => AnswerAsync(request, ct);

        /// <summary>The server's answer to one request (for a fake that plays more of the web around it).</summary>
        public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var headerMissing = RequiredHeader is var (name, value) &&
                !(request.Headers.TryGetValues(name, out var sent) && sent.Contains(value));
            if (headerMissing || (Accepts != null && !Accepts(request)))
            {
                var refused = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (Challenge != null) refused.Headers.TryAddWithoutValidation("WWW-Authenticate", Challenge);
                return refused;
            }
            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

            var message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var method = message["method"]?.GetValue<string>() ?? "";
            Methods.Add(method);
            Authorizations.Add(request.Headers.Authorization?.ToString());
            if (method == "tools/call" && CallsNeedScope is { } needed)
            {
                var forbidden = new HttpResponseMessage(HttpStatusCode.Forbidden);
                forbidden.Headers.TryAddWithoutValidation("WWW-Authenticate", $"Bearer error=\"insufficient_scope\", scope=\"{needed}\"");
                return forbidden;
            }
            if (message["id"] is not { } id) return new HttpResponseMessage(HttpStatusCode.Accepted);

            JsonNode result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = message["params"]!["protocolVersion"]!.GetValue<string>(),
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "fake", ["title"] = Title, ["version"] = "1.0" },
                },
                "tools/list" => JsonNode.Parse(ToolsJson)!,
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
}
