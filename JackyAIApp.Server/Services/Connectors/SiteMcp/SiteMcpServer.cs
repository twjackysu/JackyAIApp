using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackyAIApp.Server.Services.Connectors.SiteMcp
{
    /// <summary>What to answer one MCP message with: its HTTP status and, unless it was a notification, the JSON-RPC body.</summary>
    public sealed record SiteMcpReply(int StatusCode, JsonObject? Body)
    {
        /// <summary>A notification or a client's response: taken, nothing to say (Streamable HTTP's 202).</summary>
        public static readonly SiteMcpReply Accepted = new(StatusCodes.Status202Accepted, null);
    }

    /// <summary>
    /// The site's own MCP server (docs/ai-native/design/03 §9): another AI — Claude Desktop, Claude Code, Cursor, ChatGPT —
    /// uses the site's tools (every <see cref="ISiteTool"/> a feature registers) in the name of the reader whose token it
    /// holds. Streamable HTTP without sessions: every POST is one JSON-RPC message answered with JSON, which the transport
    /// allows (no SSE, no server-to-client requests). The methods are <c>initialize</c>, <c>ping</c>, <c>tools/list</c>
    /// and <c>tools/call</c>.
    /// <para>
    /// Which tools are offered: the reader's permission for each (the connectors page) — one they turned off is not
    /// offered — and the token's: a read always, a write only with a token that allows writes, something that cannot be
    /// undone never. The site's own confirmation card is not there to ask, so the other client's own asking is what
    /// stands between its model and a write, and a token that allows none is the default.
    /// </para>
    /// </summary>
    public interface ISiteMcpServer
    {
        /// <param name="siteOrigin">
        /// The site's public origin (<see cref="SiteOAuthUrls.Issuer"/>): a tool's link to a page of the site is a path,
        /// which means nothing to another client, so the result names it in full.
        /// </param>
        Task<SiteMcpReply> HandleAsync(JsonNode? message, SiteTokenHolder holder, string siteOrigin, CancellationToken ct);
    }

    public sealed class SiteMcpServer(
        IEnumerable<ISiteTool> siteTools, IToolPermissionSource permissions, TimeProvider time, ILogger<SiteMcpServer> logger)
        : ISiteMcpServer
    {
        /// <summary>The protocol versions this server speaks, newest first; one a client asks for is answered in kind.</summary>
        public static readonly IReadOnlyList<string> ProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26"];

        public const string ServerName = "jacky-studio";
        public const string ServerTitle = "Jacky Studio";

        /// <summary>How long one call may take (as in the site's own chat).</summary>
        public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The most of one result sent back; the rest is cut, and the client is told so.</summary>
        public const int MaxResultChars = 40_000;

        internal const string Instructions =
            "Jacky Studio（https://apps.jacky-studio.com）的工具：英文字典與單字庫、學習進度、投資組合摘要、Google 日曆。" +
            "所有資料都屬於這個 token 的主人；只在他要求時讀取或變更。";

        internal const string FailedMessage = "工具執行失敗，請換個方式或稍後再試。";
        internal const string TimedOutMessage = "工具逾時沒有回應，請稍後再試。";
        internal const string TruncatedNote = "\n[結果過長，以上僅為前段。]";

        // JSON-RPC 2.0 error codes.
        internal const int InvalidRequest = -32600;
        internal const int MethodNotFound = -32601;
        internal const int InvalidParams = -32602;
        /// <summary>MCP's own code for a resource that is not there.</summary>
        internal const int ResourceNotFound = -32002;

        private readonly IReadOnlyList<ISiteTool> _tools = siteTools.ToList();

        public async Task<SiteMcpReply> HandleAsync(JsonNode? message, SiteTokenHolder holder, string siteOrigin, CancellationToken ct)
        {
            // One message a POST: JSON-RPC batches were dropped from MCP in 2025-06-18.
            if (message is not JsonObject request || request["jsonrpc"]?.GetValueKind() != JsonValueKind.String ||
                (string?)request["jsonrpc"] != "2.0")
                return Error(null, InvalidRequest, "Expected one JSON-RPC 2.0 message.");

            var id = request["id"];
            var method = request["method"]?.GetValueKind() == JsonValueKind.String ? (string?)request["method"] : null;
            if (method == null)
            {
                // A response to a request of ours (we send none), or junk: nothing to answer either way.
                return id != null && (request.ContainsKey("result") || request.ContainsKey("error"))
                    ? SiteMcpReply.Accepted
                    : Error(id, InvalidRequest, "A request needs a method.");
            }
            if (id == null) return SiteMcpReply.Accepted; // a notification (notifications/initialized, cancelled…)
            if (id.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
                return Error(null, InvalidRequest, "A request's id is a string or a number.");

            var parameters = request["params"] as JsonObject;
            return method switch
            {
                "initialize" => Result(id, Initialize(parameters)),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, await ListToolsAsync(holder, ct)),
                "tools/call" => await CallToolAsync(id, parameters, holder, siteOrigin, ct),
                "resources/list" => Result(id, new JsonObject { ["resources"] = new JsonArray(SiteMcpApp.ResourceJson()) }),
                "resources/read" => ReadResource(id, parameters),
                _ => Error(id, MethodNotFound, $"Method not found: {method}"),
            };
        }

        private static JsonObject Initialize(JsonObject? parameters)
        {
            var asked = parameters?["protocolVersion"]?.GetValueKind() == JsonValueKind.String
                ? (string?)parameters["protocolVersion"]
                : null;
            var version = asked != null && ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0];
            return new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                    // The one resource is the MCP App drawing the tools' cards (SiteMcpApp).
                    ["resources"] = new JsonObject { ["listChanged"] = false },
                    ["extensions"] = new JsonObject
                    {
                        [SiteMcpApp.ExtensionKey] = new JsonObject { ["mimeTypes"] = new JsonArray(SiteMcpApp.MimeType) },
                    },
                },
                ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["title"] = ServerTitle, ["version"] = "1.0" },
                ["instructions"] = Instructions,
            };
        }

        /// <summary>The tools this holder may use, as the reader and the token allow (see the interface).</summary>
        internal async Task<IReadOnlyList<ISiteTool>> OfferedAsync(SiteTokenHolder holder, CancellationToken ct)
        {
            var chosen = await permissions.ChosenAsync(holder.UserId, SiteConnector.Key, ct);
            return _tools.Where(t => Offered(t.Descriptor, chosen, holder.AllowWrites)).ToList();
        }

        internal static bool Offered(AiToolDescriptor tool, IReadOnlyDictionary<string, ToolPermission> chosen, bool allowWrites) =>
            ToolPermissions.For(tool.Name, tool.Effect, chosen) != ToolPermission.Disabled && tool.Effect switch
            {
                ToolEffect.Read => true,
                ToolEffect.Write => allowWrites,
                _ => false,
            };

        private async Task<JsonObject> ListToolsAsync(SiteTokenHolder holder, CancellationToken ct)
        {
            var tools = new JsonArray();
            foreach (var tool in await OfferedAsync(holder, ct)) tools.Add(ToolJson(tool.Descriptor));
            return new JsonObject { ["tools"] = tools };
        }

        internal static JsonObject ToolJson(AiToolDescriptor tool) => new()
        {
            ["name"] = tool.Name,
            ["title"] = tool.DisplayName,
            ["description"] = tool.Description,
            ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            ["annotations"] = new JsonObject
            {
                ["title"] = tool.DisplayName,
                ["readOnlyHint"] = tool.Effect == ToolEffect.Read,
                ["destructiveHint"] = tool.Effect == ToolEffect.Destructive,
                // Only a tool reading other people's words (a calendar invite) reaches past the reader's own data.
                ["openWorldHint"] = tool.OutsideText,
            },
            // Every site tool's result has a card; a client drawing MCP Apps shows it with this app.
            ["_meta"] = SiteMcpApp.ToolMeta(),
        };

        private static SiteMcpReply ReadResource(JsonNode id, JsonObject? parameters)
        {
            var uri = parameters?["uri"]?.GetValueKind() == JsonValueKind.String ? (string?)parameters["uri"] : null;
            if (uri == null) return Error(id, InvalidParams, "resources/read needs a uri.");
            return uri == SiteMcpApp.ResourceUri
                ? Result(id, SiteMcpApp.ReadResult())
                : Error(id, ResourceNotFound, $"Resource not found: {uri}");
        }

        private async Task<SiteMcpReply> CallToolAsync(
            JsonNode id, JsonObject? parameters, SiteTokenHolder holder, string siteOrigin, CancellationToken ct)
        {
            var name = parameters?["name"]?.GetValueKind() == JsonValueKind.String ? (string?)parameters["name"] : null;
            if (name == null) return Error(id, InvalidParams, "tools/call needs a tool name.");
            // A tool this holder may not use is answered as one that does not exist: what is withheld is not told.
            var tool = (await OfferedAsync(holder, ct)).FirstOrDefault(t => t.Descriptor.Name == name);
            if (tool == null) return Error(id, InvalidParams, $"Unknown tool: {name}");

            var arguments = parameters!["arguments"] switch
            {
                null => JsonSerializer.SerializeToElement(new JsonObject()),
                JsonObject args => JsonSerializer.SerializeToElement(args),
                _ => (JsonElement?)null,
            };
            if (arguments is not { } input) return Error(id, InvalidParams, "A tool's arguments are an object.");

            var started = time.GetTimestamp();
            var outcome = await RunAsync(tool, input, holder, ct);
            logger.LogInformation("Site MCP tool {Tool} for token {TokenId}: {Outcome} in {Ms} ms",
                name, holder.TokenId, outcome.IsError ? "error" : "ok", (long)time.GetElapsedTime(started).TotalMilliseconds);

            var text = outcome.Content.Length > MaxResultChars ? outcome.Content[..MaxResultChars] + TruncatedNote : outcome.Content;
            if (!outcome.IsError && LinkLine(outcome.Card, siteOrigin) is { } link) text += "\n\n" + link;
            var result = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["isError"] = outcome.IsError,
            };
            if (!outcome.IsError && SiteMcpApp.StructuredContent(outcome.Card, siteOrigin) is { } structured)
                result["structuredContent"] = structured;
            return Result(id, result);
        }

        /// <summary>
        /// A link card as a line of text with the address in full (<c>標題：https://…</c>), for the other client's model to
        /// give the reader: the card itself is drawn only by the site's own chat, and its <c>href</c> is usually a path.
        /// Null for any other card, or a link that is neither a path on the site nor https.
        /// </summary>
        internal static string? LinkLine(object? card, string siteOrigin)
        {
            if (card == null || JsonSerializer.SerializeToNode(card) is not JsonObject node) return null;
            if (StringOf(node["kind"]) != "link" || AbsoluteHref(StringOf(node["href"]), siteOrigin) is not { } url) return null;

            var title = StringOf(node["title"])?.Trim();
            return string.IsNullOrEmpty(title) ? url : $"{title}：{url}";
        }

        /// <summary>A card's link in full: a path on the site after its origin, an https address as it is; else null.</summary>
        public static string? AbsoluteHref(string? href, string siteOrigin)
        {
            href = href?.Trim();
            if (string.IsNullOrEmpty(href)) return null;
            if (href.StartsWith('/'))
                return href.StartsWith("//") || href.StartsWith("/\\") ? null : siteOrigin.TrimEnd('/') + href;
            return Uri.TryCreate(href, UriKind.Absolute, out var absolute) && absolute.Scheme == Uri.UriSchemeHttps
                ? absolute.AbsoluteUri
                : null;
        }

        private static string? StringOf(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        private async Task<AiToolResult> RunAsync(ISiteTool tool, JsonElement input, SiteTokenHolder holder, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);
            try
            {
                // A conversation of its own per token: a tool keeping something per conversation keeps it per client.
                return await tool.RunAsync(input, new AiToolContext(holder.UserId, $"mcp:{holder.TokenId}"), timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return AiToolResult.Error(TimedOutMessage);
            }
            catch (Exception ex)
            {
                // Never the exception's text: it may say more about the site than the caller should know.
                logger.LogError(ex, "Site MCP tool {Tool} failed", tool.Descriptor.Name);
                return AiToolResult.Error(FailedMessage);
            }
        }

        private static SiteMcpReply Result(JsonNode id, JsonObject result) => new(StatusCodes.Status200OK, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result,
        });

        /// <summary>A JSON-RPC error; one that is not about a request with an id is also an HTTP 400.</summary>
        internal static SiteMcpReply Error(JsonNode? id, int code, string message) => new(
            id == null ? StatusCodes.Status400BadRequest : StatusCodes.Status200OK,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
            });
    }
}
