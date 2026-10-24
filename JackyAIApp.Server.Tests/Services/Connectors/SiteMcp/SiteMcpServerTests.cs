using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Connectors.SiteMcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Connectors.SiteMcp
{
    /// <summary>
    /// The site's own MCP server: the protocol (JSON-RPC over Streamable HTTP, answered with JSON), which tools a token
    /// may see and call, and the real SDK's client talking to it end to end.
    /// </summary>
    public class SiteMcpServerTests
    {
        private static readonly SiteTokenHolder ReadOnly = new("u1", "t1", AllowWrites: false);
        private static readonly SiteTokenHolder WithWrites = new("u1", "t2", AllowWrites: true);
        private const string Origin = "https://apps.jacky-studio.com";

        private sealed class FakeTool(string name, ToolEffect effect, Func<JsonElement, AiToolContext, AiToolResult>? run = null, bool outsideText = false)
            : ISiteTool
        {
            public AiToolDescriptor Descriptor { get; } = new(name, $"{name} 顯示名", $"{name} 說明",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { word = new { type = "string" } } }),
                effect, OutsideText: outsideText);

            public List<(JsonElement Input, AiToolContext Context)> Calls { get; } = [];

            public Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
            {
                Calls.Add((input.Clone(), context));
                return Task.FromResult(run?.Invoke(input, context) ?? new AiToolResult($"{name} 好了"));
            }
        }

        private readonly FakeTool _lookUp = new("look_up_word", ToolEffect.Read,
            (input, _) => new AiToolResult($"{input.GetProperty("word").GetString()}：意外發現的好運"));
        private readonly FakeTool _calendar = new("calendar", ToolEffect.Read, outsideText: true);
        private readonly FakeTool _addWord = new("add_word", ToolEffect.Write);
        private readonly FakeTool _clear = new("clear_words", ToolEffect.Destructive);
        private readonly FakeTool _linked = new("open_repository", ToolEffect.Read,
            (_, _) => new AiToolResult("單字庫有 3 個字。", Card: new { kind = "link", title = "我的單字庫", href = "/english/repository" }));
        private readonly FakeTool _table = new("portfolio", ToolEffect.Read,
            (_, _) => new AiToolResult("2 筆持倉。", Card: new
            {
                kind = "table",
                title = "投資組合",
                columns = new object[] { new { key = "name", label = "持倉" }, new { key = "value", label = "市值", numeric = true } },
                rows = new object[] { new Dictionary<string, object?> { ["name"] = "台積電", ["value"] = 1000 } },
            }));
        private readonly FakeTool _broken = new("broken", ToolEffect.Read, (_, _) => throw new InvalidOperationException("connection string Server=secret"));

        private Dictionary<string, ToolPermission> _chosen = [];

        private SiteMcpServer Server()
        {
            var permissions = new Mock<IToolPermissionSource>();
            permissions.Setup(p => p.ChosenAsync("u1", SiteConnector.Key, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _chosen);
            return new SiteMcpServer([_lookUp, _calendar, _addWord, _clear, _linked, _table, _broken], permissions.Object,
                TimeProvider.System, NullLogger<SiteMcpServer>.Instance);
        }

        private static JsonObject Request(string method, JsonObject? parameters = null, JsonNode? id = null)
        {
            var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id ?? JsonValue.Create(1), ["method"] = method };
            if (parameters != null) request["params"] = parameters;
            return request;
        }

        private async Task<JsonObject> ResultOf(JsonObject request, SiteTokenHolder? holder = null)
        {
            var reply = await Server().HandleAsync(request, holder ?? ReadOnly, Origin, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, reply.StatusCode);
            var body = reply.Body!;
            Assert.Null(body["error"]);
            return body["result"]!.AsObject();
        }

        private async Task<List<string>> ToolNamesFor(SiteTokenHolder holder) =>
            (await ResultOf(Request("tools/list"), holder))["tools"]!.AsArray().Select(t => (string)t!["name"]!).ToList();

        private static JsonObject Call(string tool, JsonObject? arguments = null) =>
            Request("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments ?? new JsonObject() });

        [Theory]
        [InlineData("2025-06-18", "2025-06-18")]
        [InlineData("2025-03-26", "2025-03-26")]
        [InlineData("2099-01-01", "2025-11-25")]
        public async Task Initialize_AnswersTheVersionAskedFor_ElseItsNewest(string asked, string answered)
        {
            var result = await ResultOf(Request("initialize", new JsonObject { ["protocolVersion"] = asked, ["capabilities"] = new JsonObject() }));

            Assert.Equal(answered, (string?)result["protocolVersion"]);
            Assert.Equal("Jacky Studio", (string?)result["serverInfo"]!["title"]);
            Assert.Equal("https://apps.jacky-studio.com/", (string?)result["serverInfo"]!["websiteUrl"]);
            Assert.Equal("https://apps.jacky-studio.com/apple-touch-icon.png", (string?)result["serverInfo"]!["icons"]![0]!["src"]);
            Assert.NotNull(result["capabilities"]!["tools"]);
            Assert.Contains("Jacky Studio", (string?)result["instructions"]);
        }

        [Fact]
        public async Task TheRequestsId_ComesBackAsSent()
        {
            var reply = await Server().HandleAsync(Request("ping", id: "abc-1"), ReadOnly, Origin, CancellationToken.None);

            var body = reply.Body!;
            Assert.Equal("abc-1", (string?)body["id"]);
            Assert.Equal("2.0", (string?)body["jsonrpc"]);
            Assert.Empty(body["result"]!.AsObject());
        }

        [Fact]
        public async Task ANotification_IsTaken_WithNothingToSay()
        {
            var notification = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" };

            var reply = await Server().HandleAsync(notification, ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(StatusCodes.Status202Accepted, reply.StatusCode);
            Assert.Null(reply.Body);
        }

        [Fact]
        public async Task AnUnknownMethod_IsMethodNotFound()
        {
            var reply = await Server().HandleAsync(Request("prompts/list"), ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(SiteMcpServer.MethodNotFound, (int)reply.Body!["error"]!["code"]!);
        }

        public static TheoryData<string> NotOneMessage => new()
        {
            """[{ "jsonrpc": "2.0", "id": 1, "method": "ping" }]""",
            """{ "id": 1, "method": "ping" }""",
            """{ "jsonrpc": "1.0", "id": 1, "method": "ping" }""",
            """{ "jsonrpc": "2.0", "id": { "x": 1 }, "method": "ping" }""",
            "42",
        };

        [Theory]
        [MemberData(nameof(NotOneMessage))]
        public async Task SomethingThatIsNotOneJsonRpcRequest_IsAnInvalidRequest(string json)
        {
            var reply = await Server().HandleAsync(JsonNode.Parse(json), ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(StatusCodes.Status400BadRequest, reply.StatusCode);
            Assert.Equal(SiteMcpServer.InvalidRequest, (int)reply.Body!["error"]!["code"]!);
        }

        [Fact]
        public async Task AReadOnlyToken_SeesTheReads_Only()
        {
            Assert.Equal(new[] { "look_up_word", "calendar", "open_repository", "portfolio", "broken" }, await ToolNamesFor(ReadOnly));
        }

        [Fact]
        public async Task ATokenThatAllowsWrites_SeesTheWritesToo_ButNeverWhatCannotBeUndone()
        {
            Assert.Equal(new[] { "look_up_word", "calendar", "add_word", "open_repository", "portfolio", "broken" }, await ToolNamesFor(WithWrites));
        }

        [Fact]
        public async Task AToolTheReaderTurnedOff_IsNotOffered()
        {
            _chosen = new() { ["look_up_word"] = ToolPermission.Disabled, ["add_word"] = ToolPermission.Disabled };

            Assert.Equal(new[] { "calendar", "open_repository", "portfolio", "broken" }, await ToolNamesFor(WithWrites));
        }

        [Fact]
        public async Task ATool_IsDescribedWithItsSchemaAndHints()
        {
            var tools = (await ResultOf(Request("tools/list"), WithWrites))["tools"]!.AsArray();
            var lookUp = tools.Single(t => (string?)t!["name"] == "look_up_word")!;
            var add = tools.Single(t => (string?)t!["name"] == "add_word")!;
            var calendar = tools.Single(t => (string?)t!["name"] == "calendar")!;

            Assert.Equal("look_up_word 顯示名", (string?)lookUp["title"]);
            Assert.Equal("look_up_word 說明", (string?)lookUp["description"]);
            Assert.Equal("string", (string?)lookUp["inputSchema"]!["properties"]!["word"]!["type"]);
            Assert.True((bool)lookUp["annotations"]!["readOnlyHint"]!);
            Assert.False((bool)lookUp["annotations"]!["openWorldHint"]!);
            Assert.False((bool)add["annotations"]!["readOnlyHint"]!);
            Assert.False((bool)add["annotations"]!["destructiveHint"]!);
            Assert.True((bool)calendar["annotations"]!["openWorldHint"]!);
        }

        [Fact]
        public async Task ACall_RunsTheTool_ForTheTokensOwner()
        {
            var result = await ResultOf(Call("look_up_word", new JsonObject { ["word"] = "serendipity" }));

            Assert.False((bool)result["isError"]!);
            Assert.Equal("serendipity：意外發現的好運", (string?)result["content"]![0]!["text"]);
            Assert.Equal("text", (string?)result["content"]![0]!["type"]);
            var (input, context) = Assert.Single(_lookUp.Calls);
            Assert.Equal("serendipity", input.GetProperty("word").GetString());
            Assert.Equal(new AiToolContext("u1", "mcp:t1"), context);
        }

        [Fact]
        public async Task ACallWithoutArguments_GetsAnEmptyObject()
        {
            await ResultOf(Request("tools/call", new JsonObject { ["name"] = "calendar" }));

            Assert.Equal(JsonValueKind.Object, Assert.Single(_calendar.Calls).Input.ValueKind);
        }

        [Fact]
        public async Task AWrite_AReadOnlyTokenCannotSee_CannotBeCalledEither()
        {
            var reply = await Server().HandleAsync(Call("add_word"), ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(SiteMcpServer.InvalidParams, (int)reply.Body!["error"]!["code"]!);
            Assert.Empty(_addWord.Calls);

            await ResultOf(Call("add_word"), WithWrites);
            Assert.Single(_addWord.Calls);
        }

        [Theory]
        [InlineData("clear_words")]
        [InlineData("no_such_tool")]
        public async Task AToolNotOffered_IsAnUnknownTool(string tool)
        {
            var reply = await Server().HandleAsync(Call(tool), WithWrites, Origin, CancellationToken.None);

            Assert.Equal(SiteMcpServer.InvalidParams, (int)reply.Body!["error"]!["code"]!);
            Assert.Empty(_clear.Calls);
        }

        [Fact]
        public async Task ArgumentsThatAreNotAnObject_AreInvalidParams()
        {
            var reply = await Server().HandleAsync(
                Request("tools/call", new JsonObject { ["name"] = "look_up_word", ["arguments"] = new JsonArray() }), ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(SiteMcpServer.InvalidParams, (int)reply.Body!["error"]!["code"]!);
            Assert.Empty(_lookUp.Calls);
        }

        [Fact]
        public async Task AFailingTool_IsAnErrorResult_WithoutTheExceptionsText()
        {
            var result = await ResultOf(Call("broken"));

            Assert.True((bool)result["isError"]!);
            var text = (string?)result["content"]![0]!["text"];
            Assert.Equal(SiteMcpServer.FailedMessage, text);
            Assert.DoesNotContain("secret", result.ToJsonString());
        }

        [Fact]
        public async Task ALinkCard_ComesBackAsALineWithTheAddressInFull()
        {
            var result = await ResultOf(Call("open_repository"));

            Assert.Equal("單字庫有 3 個字。\n\n我的單字庫：https://apps.jacky-studio.com/english/repository",
                (string?)result["content"]![0]!["text"]);
        }

        [Theory]
        [InlineData("/english/dictionary/serendipity", "https://apps.jacky-studio.com/english/dictionary/serendipity")]
        [InlineData("https://calendar.google.com/event?eid=1", "https://calendar.google.com/event?eid=1")]
        [InlineData("//evil.example/x", null)]
        [InlineData("/\\evil.example/x", null)]
        [InlineData("http://plain.example/x", null)]
        [InlineData("javascript:alert(1)", null)]
        public void ALinksAddress_IsTheSitesPathInFull_OrHttpsOnly(string href, string? url)
        {
            var line = SiteMcpServer.LinkLine(new { kind = "link", title = "看看", href }, Origin + "/");

            Assert.Equal(url == null ? null : "看看：" + url, line);
        }

        [Fact]
        public void ACardThatIsNotALink_AddsNothing()
        {
            Assert.Null(SiteMcpServer.LinkLine(new { kind = "table", columns = new[] { "a" }, rows = Array.Empty<object>() }, Origin));
            Assert.Null(SiteMcpServer.LinkLine(null, Origin));
            Assert.Equal("https://apps.jacky-studio.com/portfolio",
                SiteMcpServer.LinkLine(JsonSerializer.SerializeToElement(new { kind = "link", href = "/portfolio" }), Origin));
        }

        // ---- MCP Apps: the card drawn by a client that draws apps ----

        [Fact]
        public async Task Initialize_DeclaresTheAppExtension_AndResources()
        {
            var result = await ResultOf(Request("initialize", new JsonObject { ["protocolVersion"] = "2025-11-25" }));

            var capabilities = result["capabilities"]!;
            Assert.NotNull(capabilities["resources"]);
            Assert.Equal(SiteMcpApp.MimeType, (string?)capabilities["extensions"]![SiteMcpApp.ExtensionKey]!["mimeTypes"]![0]);
        }

        [Fact]
        public async Task EveryTool_NamesTheApp()
        {
            var tools = (await ResultOf(Request("tools/list"), WithWrites))["tools"]!.AsArray();

            Assert.All(tools, t => Assert.Equal(SiteMcpApp.ResourceUri, (string?)t!["_meta"]!["ui"]!["resourceUri"]));
        }

        [Fact]
        public async Task TheApp_IsListed_AndReadAsAnMcpAppPage()
        {
            var listed = await ResultOf(Request("resources/list"));
            var resource = Assert.Single(listed["resources"]!.AsArray())!;
            Assert.Equal(SiteMcpApp.ResourceUri, (string?)resource["uri"]);
            Assert.Equal(SiteMcpApp.MimeType, (string?)resource["mimeType"]);

            var read = await ResultOf(Request("resources/read", new JsonObject { ["uri"] = SiteMcpApp.ResourceUri }));
            var content = Assert.Single(read["contents"]!.AsArray())!;
            Assert.Equal(SiteMcpApp.MimeType, (string?)content["mimeType"]);
            Assert.StartsWith("<!doctype html>", (string?)content["text"]);
        }

        [Fact]
        public async Task AResourceThatIsNotThere_IsResourceNotFound()
        {
            var reply = await Server().HandleAsync(
                Request("resources/read", new JsonObject { ["uri"] = "ui://jacky-studio/other.html" }), ReadOnly, Origin, CancellationToken.None);

            Assert.Equal(SiteMcpServer.ResourceNotFound, (int)reply.Body!["error"]!["code"]!);
        }

        [Fact]
        public void TheApp_DrawsText_Only_AndLoadsNothingFromOutside()
        {
            var html = SiteMcpApp.Html;

            Assert.Contains("ui/initialize", html);
            Assert.Contains("ui/notifications/initialized", html);
            Assert.Contains("ui/notifications/size-changed", html);
            Assert.Contains("ui/open-link", html);
            Assert.DoesNotContain("innerHTML", html);
            Assert.Contains("event.source !== window.parent", html);
            Assert.DoesNotContain("<script src", html);
            Assert.DoesNotContain("<link", html);
            Assert.DoesNotContain("http://", html);
        }

        [Fact]
        public async Task ALinksCard_IsTheStructuredContent_WithItsAddressInFull()
        {
            var result = await ResultOf(Call("open_repository"));

            var card = result["structuredContent"]!["card"]!;
            Assert.Equal("link", (string?)card["kind"]);
            Assert.Equal("https://apps.jacky-studio.com/english/repository", (string?)card["href"]);
        }

        [Fact]
        public async Task ATablesCard_IsTheStructuredContent()
        {
            var result = await ResultOf(Call("portfolio"));

            var card = result["structuredContent"]!["card"]!;
            Assert.Equal("table", (string?)card["kind"]);
            Assert.Equal("台積電", (string?)card["rows"]![0]!["name"]);
            Assert.Equal("2 筆持倉。", (string?)result["content"]![0]!["text"]);
        }

        [Fact]
        public async Task AResultWithoutACard_OrAFailure_HasNoStructuredContent()
        {
            Assert.Null((await ResultOf(Call("calendar")))["structuredContent"]);
            Assert.Null((await ResultOf(Call("broken")))["structuredContent"]);
        }

        [Theory]
        [InlineData("chart")]
        [InlineData("link")]
        public void ACardTheAppDoesNotDraw_OrALinkThatMayNotBeFollowed_IsLeftOut(string kind)
        {
            Assert.Null(SiteMcpApp.StructuredContent(new { kind, href = "javascript:alert(1)", data = new { } }, Origin));
        }

        // ---- The real SDK's client against this server (what Claude Desktop, Cursor and others do) ----

        /// <summary>Hands the SDK's requests to the server as the controller would.</summary>
        private sealed class Bridge(SiteMcpServer server, SiteTokenHolder holder) : HttpMessageHandler, IMcpHttpHandlerSource
        {
            public List<string> Methods { get; } = [];

            public HttpMessageHandler Create() => new Bridge(server, holder) { Shared = Methods };

            private List<string>? Shared { get; init; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Method != HttpMethod.Post) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                var message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
                (Shared ?? Methods).Add((string?)message?["method"] ?? "");
                var reply = await server.HandleAsync(message, holder, Origin, ct);
                var response = new HttpResponseMessage((HttpStatusCode)reply.StatusCode);
                if (reply.Body != null) response.Content = new StringContent(reply.Body.ToJsonString(), Encoding.UTF8, "application/json");
                return response;
            }
        }

        private static readonly Uri Endpoint = new("https://apps.jacky-studio.com/api/mcp");

        [Fact]
        public async Task TheSdksClient_ListsTheTools_WithWhatTheyDo()
        {
            var bridge = new Bridge(Server(), WithWrites);

            var listed = await new McpToolClient(bridge, NullLoggerFactory.Instance)
                .ListToolsAsync(Endpoint, McpAuth.None, CancellationToken.None);

            Assert.Equal("Jacky Studio", listed.ServerName);
            Assert.Equal(new[] { ("look_up_word", ToolEffect.Read), ("calendar", ToolEffect.Read), ("add_word", ToolEffect.Write), ("open_repository", ToolEffect.Read), ("portfolio", ToolEffect.Read), ("broken", ToolEffect.Read) },
                listed.Tools.Select(t => (t.Name, t.Effect)));
            Assert.Equal("initialize", bridge.Methods[0]);
            Assert.Contains("tools/list", bridge.Methods);
        }

        [Fact]
        public async Task TheSdksClient_CallsATool()
        {
            var bridge = new Bridge(Server(), ReadOnly);

            var outcome = await new McpToolClient(bridge, NullLoggerFactory.Instance).CallToolAsync(Endpoint, McpAuth.None, "look_up_word",
                JsonSerializer.SerializeToElement(new { word = "serendipity" }), CancellationToken.None);

            Assert.False(outcome.IsError);
            Assert.Equal("serendipity：意外發現的好運", outcome.Text);
        }
    }
}
