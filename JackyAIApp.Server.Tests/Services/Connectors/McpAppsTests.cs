using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// The reader's connectors' MCP Apps (docs/ai-native/design/09): what a tool's <c>_meta.ui</c> is kept as, the app's
    /// HTML read with <c>resources/read</c>, the card a call of such a tool shows, and who may read an app.
    /// </summary>
    public class McpAppsTests
    {
        private static readonly Uri Server = new("https://mcp.example.com/mcp");
        private const string AppUri = "ui://weather/forecast.html";

        private const string ToolsWithApp = """
            { "tools": [
              { "name": "forecast", "title": "天氣預報", "inputSchema": { "type": "object" },
                "annotations": { "readOnlyHint": true },
                "_meta": { "ui": { "resourceUri": "ui://weather/forecast.html" } } },
              { "name": "refresh_forecast", "inputSchema": { "type": "object" }, "annotations": { "readOnlyHint": true },
                "_meta": { "ui": { "resourceUri": "ui://weather/forecast.html", "visibility": ["app"] } } },
              { "name": "plain", "inputSchema": { "type": "object" },
                "_meta": { "ui": { "resourceUri": "https://not-an-app.example.com/x" } } }
            ] }
            """;

        private static string ResourceJson(string mimeType = McpToolClient.AppMimeType, string html = "<html><body>app</body></html>",
            string uri = AppUri) => new JsonObject
            {
                ["contents"] = new JsonArray(new JsonObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = mimeType,
                    ["text"] = html,
                    ["_meta"] = new JsonObject
                    {
                        ["ui"] = new JsonObject
                        {
                            ["prefersBorder"] = true,
                            ["csp"] = new JsonObject
                            {
                                ["connectDomains"] = new JsonArray("https://api.weather.example.com", "http://plain.example.com",
                                    "https://*.example.com", "https://a.example.com/path", "https://b.example.com 'unsafe-eval'"),
                                ["resourceDomains"] = new JsonArray("https://cdn.example.com:8443"),
                            },
                        },
                    },
                }),
            }.ToJsonString();

        private static McpToolClient Client(FakeMcpServer server) => new(server, NullLoggerFactory.Instance);

        [Fact]
        public async Task AToolsApp_IsKept_AndOneForTheAppAlone_IsMarked_ButNotAnAddressThatIsNotUi()
        {
            var listed = await Client(new FakeMcpServer { ToolsJson = ToolsWithApp })
                .ListToolsAsync(Server, McpAuth.None, CancellationToken.None);

            Assert.Equal(AppUri, listed.Tools[0].UiResourceUri);
            Assert.False(listed.Tools[0].AppOnly);
            Assert.True(listed.Tools[1].AppOnly);
            Assert.Null(listed.Tools[2].UiResourceUri);
        }

        [Fact]
        public void TheStoredTools_KeepTheApp_AndAToolWithoutOne_IsStoredAsBefore()
        {
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            var plain = new CachedTool("a", null, "", ToolEffect.Read, schema);
            var withApp = new CachedTool("b", null, "", ToolEffect.Read, schema, AppUri, AppOnly: true);

            var json = CachedTools.Serialize([plain, withApp]);
            var back = CachedTools.Parse(json);

            Assert.DoesNotContain("uiResourceUri\":null", json);
            Assert.Equal(1, json.Split("uiResourceUri").Length - 1);
            Assert.Null(back[0].UiResourceUri);
            Assert.False(back[0].AppOnly);
            Assert.Equal(AppUri, back[1].UiResourceUri);
            Assert.True(back[1].AppOnly);
        }

        [Fact]
        public void AStoredAppAddressThatIsNotUi_IsDropped()
        {
            var back = CachedTools.Parse("""[{ "name": "a", "effect": "Read", "uiResourceUri": "javascript:alert(1)" }]""");

            Assert.Null(back[0].UiResourceUri);
        }

        [Fact]
        public async Task TheApp_IsRead_WithOnlyHttpsOriginsForItsCsp()
        {
            var server = new FakeMcpServer { Resources = { [AppUri] = ResourceJson() } };

            var app = await Client(server).ReadAppAsync(Server, McpAuth.None, AppUri, CancellationToken.None);

            Assert.NotNull(app);
            Assert.Equal("<html><body>app</body></html>", app.Html);
            Assert.Equal(new[] { "https://api.weather.example.com" }, app.ConnectDomains);
            Assert.Equal(new[] { "https://cdn.example.com:8443" }, app.ResourceDomains);
            Assert.True(app.PrefersBorder);
            Assert.Contains("resources/read", server.Methods);
        }

        [Theory]
        [InlineData("text/html")]
        [InlineData("text/plain")]
        public async Task AResourceOfAnotherType_IsNotAnApp(string mimeType)
        {
            var server = new FakeMcpServer { Resources = { [AppUri] = ResourceJson(mimeType) } };

            Assert.Null(await Client(server).ReadAppAsync(Server, McpAuth.None, AppUri, CancellationToken.None));
        }

        [Fact]
        public async Task AnAppTooLarge_OrForAnotherUri_IsNotDrawn()
        {
            var large = new FakeMcpServer { Resources = { [AppUri] = ResourceJson(html: new string('x', McpToolClient.MaxAppHtmlChars + 1)) } };
            var other = new FakeMcpServer { Resources = { [AppUri] = ResourceJson(uri: "ui://weather/other.html") } };

            Assert.Null(await Client(large).ReadAppAsync(Server, McpAuth.None, AppUri, CancellationToken.None));
            Assert.Null(await Client(other).ReadAppAsync(Server, McpAuth.None, AppUri, CancellationToken.None));
        }

        [Fact]
        public async Task AnAddressThatIsNotUi_IsNotAskedFor()
        {
            var server = new FakeMcpServer();

            Assert.Null(await Client(server).ReadAppAsync(Server, McpAuth.None, "https://evil.example.com/", CancellationToken.None));
            Assert.Empty(server.Methods);
        }

        [Fact]
        public async Task ACallsResult_IsHandedToTheApp_WithItsStructuredContent()
        {
            var server = new FakeMcpServer
            {
                CallResultJson = """
                    { "content": [ { "type": "text", "text": "晴" }, { "type": "image", "data": "AQ==", "mimeType": "image/png" } ],
                      "structuredContent": { "city": "台北", "high": 28 } }
                    """,
            };

            var outcome = await Client(server).CallToolAsync(Server, McpAuth.None, "forecast",
                JsonSerializer.SerializeToElement(new { city = "台北" }), CancellationToken.None);

            var app = JsonNode.Parse(outcome.AppResultJson!)!;
            Assert.Equal("晴\n[image]", outcome.Text);
            Assert.Single(app["content"]!.AsArray());
            Assert.Equal("晴", app["content"]![0]!["text"]!.GetValue<string>());
            Assert.Equal(28, app["structuredContent"]!["high"]!.GetValue<int>());
        }

        [Fact]
        public void AResultTooLargeForTheApp_IsNotHandedToIt()
        {
            var result = new ModelContextProtocol.Protocol.CallToolResult
            {
                Content = [new ModelContextProtocol.Protocol.TextContentBlock { Text = new string('x', McpToolClient.MaxAppResultChars) }],
            };

            Assert.Null(McpToolClient.AppResultOf(result));
        }

        // --- the chat: a tool with an app shows an mcp-app card; one for the app alone is not offered ---

        private readonly DbContextOptions<AzureSQLDBContext> _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private string AddInstallation(string userId, string toolsJson)
        {
            using var db = new AzureSQLDBContext(_options);
            var row = new SQL.ConnectorInstallation
            {
                UserId = userId,
                DefinitionKey = SQL.ConnectorInstallation.CustomKey,
                DisplayName = "天氣",
                ServerUrl = Server.ToString(),
                Enabled = true,
                ToolsCacheJson = toolsJson,
            };
            db.ConnectorInstallations.Add(row);
            db.SaveChanges();
            return row.Id;
        }

        private static string Cached(params CachedTool[] tools) => CachedTools.Serialize(tools);

        private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new { type = "object" });

        [Fact]
        public async Task AToolForTheAppAlone_IsNotOfferedToTheModel()
        {
            AddInstallation("u1", Cached(
                new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri),
                new CachedTool("refresh_forecast", null, "", ToolEffect.Read, Schema, AppUri, AppOnly: true)));
            using var db = new AzureSQLDBContext(_options);

            var tools = await new ConnectorToolSource(db, Mock.Of<IMcpToolClient>(), Mock.Of<IMcpOAuthTokens>(),
                NullLogger<ConnectorToolSource>.Instance).ForUserAsync("u1", default);

            Assert.Equal(new[] { "forecast" }, tools.Select(t => t.Descriptor.PermissionKey));
        }

        [Fact]
        public async Task ACallOfAToolWithAnApp_ShowsAnMcpAppCard_WithoutItsHtml()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", "天氣預報", "", ToolEffect.Read, Schema, AppUri)));
            var mcp = new Mock<IMcpToolClient>();
            mcp.Setup(m => m.CallToolAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), "forecast", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpToolOutcome("晴", false, """{"content":[{"type":"text","text":"晴"}]}"""));
            var tokens = new Mock<IMcpOAuthTokens>();
            tokens.Setup(t => t.AuthOfAsync(It.IsAny<SQL.ConnectorInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(McpAuth.None);
            using var db = new AzureSQLDBContext(_options);
            var tool = (await new ConnectorToolSource(db, mcp.Object, tokens.Object, NullLogger<ConnectorToolSource>.Instance)
                .ForUserAsync("u1", default)).Single();

            var result = await tool.RunAsync(JsonSerializer.SerializeToElement(new { city = "台北" }), null!, default);

            var card = JsonSerializer.SerializeToNode(result.Card)!;
            Assert.Equal("晴", result.Content);
            Assert.Equal("mcp-app", card["kind"]!.GetValue<string>());
            Assert.Equal(id, card["data"]!["installationId"]!.GetValue<string>());
            Assert.Equal(AppUri, card["data"]!["resourceUri"]!.GetValue<string>());
            Assert.Equal("天氣預報", card["data"]!["title"]!.GetValue<string>());
            Assert.Equal("台北", card["data"]!["input"]!["city"]!.GetValue<string>());
            Assert.Equal("晴", card["data"]!["result"]!["content"]![0]!["text"]!.GetValue<string>());
            Assert.DoesNotContain("<html", card.ToJsonString());
        }

        [Fact]
        public async Task ACallOfAToolWithoutAnApp_ShowsNoCard()
        {
            AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema)));
            var mcp = new Mock<IMcpToolClient>();
            mcp.Setup(m => m.CallToolAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), "forecast", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpToolOutcome("晴", false, """{"content":[]}"""));
            var tokens = new Mock<IMcpOAuthTokens>();
            tokens.Setup(t => t.AuthOfAsync(It.IsAny<SQL.ConnectorInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(McpAuth.None);
            using var db = new AzureSQLDBContext(_options);
            var tool = (await new ConnectorToolSource(db, mcp.Object, tokens.Object, NullLogger<ConnectorToolSource>.Instance)
                .ForUserAsync("u1", default)).Single();

            var result = await tool.RunAsync(JsonSerializer.SerializeToElement(new { }), null!, default);

            Assert.Null(result.Card);
        }

        // --- reading an app for the chat (GET installations/{id}/app) ---

        private McpAppService Apps(string? userId, IMcpToolClient mcp, AzureSQLDBContext db)
        {
            var http = new DefaultHttpContext();
            if (userId != null)
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
            var tokens = new Mock<IMcpOAuthTokens>();
            tokens.Setup(t => t.AuthOfAsync(It.IsAny<SQL.ConnectorInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(McpAuth.None);
            return new McpAppService(db, TestCurrentUser.Of(http), mcp, tokens.Object, NullLogger<McpAppService>.Instance);
        }

        private static Mock<IMcpToolClient> Reading(McpAppResource? app)
        {
            var mcp = new Mock<IMcpToolClient>();
            mcp.Setup(m => m.ReadAppAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), AppUri, It.IsAny<CancellationToken>())).ReturnsAsync(app);
            return mcp;
        }

        [Fact]
        public async Task TheReadersOwnConnectorsApp_IsRead_WithWhoseItIs()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri)));
            var mcp = Reading(new McpAppResource("<html></html>", ["https://api.example.com"], [], true));
            using var db = new AzureSQLDBContext(_options);

            var result = await Apps("u1", mcp.Object, db).GetAsync(id, AppUri, default);

            Assert.True(result.IsSuccess);
            Assert.Equal("<html></html>", result.Data!.Html);
            Assert.Equal("天氣", result.Data.Connector);
            Assert.Equal("mcp.example.com", result.Data.Host);
            Assert.Equal(new[] { "https://api.example.com" }, result.Data.ConnectDomains);
        }

        [Fact]
        public async Task SomeoneElsesConnector_OrAUriNoToolNamed_IsNotFound_AndTheServerIsNotAsked()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri)));
            var mcp = Reading(new McpAppResource("<html></html>", [], [], null));
            using var db = new AzureSQLDBContext(_options);

            var someoneElse = await Apps("u2", mcp.Object, db).GetAsync(id, AppUri, default);
            var undeclared = await Apps("u1", mcp.Object, db).GetAsync(id, "ui://weather/secret.html", default);

            Assert.Equal(ErrorCodes.NotFound, someoneElse.Error);
            Assert.Equal(ErrorCodes.NotFound, undeclared.Error);
            mcp.Verify(m => m.ReadAppAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ATurnedOffConnectorsApp_IsNotRead()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri)));
            using (var setup = new AzureSQLDBContext(_options))
            {
                setup.ConnectorInstallations.Find(id)!.Enabled = false;
                setup.SaveChanges();
            }
            var mcp = Reading(new McpAppResource("<html></html>", [], [], null));
            using var db = new AzureSQLDBContext(_options);

            Assert.Equal(ErrorCodes.NotFound, (await Apps("u1", mcp.Object, db).GetAsync(id, AppUri, default)).Error);
            Assert.Equal(ErrorCodes.Forbidden, (await Apps("u1", mcp.Object, db).CallAsync(id, "forecast", NoArguments, default)).Error);
        }

        [Fact]
        public async Task OnlyAConnectorsFirstFewApps_AreDrawn_TheRestAreText()
        {
            var tools = Enumerable.Range(1, McpAppService.MaxAppsPerConnector + 1)
                .Select(i => new CachedTool($"t{i}", null, "", ToolEffect.Read, Schema, $"ui://app/{i}.html")).ToArray();
            var id = AddInstallation("u1", Cached(tools));
            var last = $"ui://app/{McpAppService.MaxAppsPerConnector + 1}.html";
            var mcp = new Mock<IMcpToolClient>();
            mcp.Setup(m => m.ReadAppAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpAppResource("<html></html>", [], [], null));
            mcp.Setup(m => m.CallToolAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpToolOutcome("ok", false, """{"content":[]}"""));
            var tokens = new Mock<IMcpOAuthTokens>();
            tokens.Setup(t => t.AuthOfAsync(It.IsAny<SQL.ConnectorInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(McpAuth.None);
            using var db = new AzureSQLDBContext(_options);

            Assert.True((await Apps("u1", mcp.Object, db).GetAsync(id, "ui://app/1.html", default)).IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, (await Apps("u1", mcp.Object, db).GetAsync(id, last, default)).Error);
            var lastTool = (await new ConnectorToolSource(db, mcp.Object, tokens.Object, NullLogger<ConnectorToolSource>.Instance)
                .ForUserAsync("u1", default)).Single(t => t.Descriptor.PermissionKey == $"t{McpAppService.MaxAppsPerConnector + 1}");
            Assert.Null((await lastTool.RunAsync(NoArguments, null!, default)).Card);
        }

        [Fact]
        public async Task AToolTheReaderMadeAskFirst_IsNotRunByItsApp()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri)));
            using (var setup = new AzureSQLDBContext(_options))
            {
                setup.ConnectorInstallations.Find(id)!.ToolSettingsJson = """{"forecast":"Confirm"}""";
                setup.SaveChanges();
            }
            var mcp = Calling("forecast");
            using var db = new AzureSQLDBContext(_options);

            Assert.Equal(ErrorCodes.Forbidden, (await Apps("u1", mcp.Object, db).CallAsync(id, "forecast", NoArguments, default)).Error);
        }

        [Fact]
        public async Task AServerWithoutTheApp_OrUnreachable_IsAFailure()
        {
            var id = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri)));
            var down = new Mock<IMcpToolClient>();
            down.Setup(m => m.ReadAppAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), AppUri, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new McpServerException(McpFailure.Unreachable, "連不上"));
            using var db = new AzureSQLDBContext(_options);

            var missing = await Apps("u1", Reading(null).Object, db).GetAsync(id, AppUri, default);
            var unreachable = await Apps("u1", down.Object, db).GetAsync(id, AppUri, default);

            Assert.Equal(ErrorCodes.ExternalApiError, missing.Error);
            Assert.Equal(ErrorCodes.ExternalApiError, unreachable.Error);
        }

        // --- a call an app asks of its server (POST installations/{id}/app-call) ---

        private static Mock<IMcpToolClient> Calling(string tool, string appResult = """{"content":[{"type":"text","text":"ok"}]}""")
        {
            var mcp = new Mock<IMcpToolClient>();
            mcp.Setup(m => m.CallToolAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), tool, It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new McpToolOutcome("ok", false, appResult));
            return mcp;
        }

        private static readonly JsonElement NoArguments = JsonSerializer.SerializeToElement(new { });

        [Fact]
        public async Task AnAppMayCall_AToolOfItsServerThatOnlyReads_AndGetsTheResult()
        {
            var id = AddInstallation("u1", Cached(
                new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri),
                new CachedTool("refresh_forecast", null, "", ToolEffect.Read, Schema, AppUri, AppOnly: true)));
            var mcp = Calling("refresh_forecast");
            using var db = new AzureSQLDBContext(_options);

            var result = await Apps("u1", mcp.Object, db).CallAsync(id, "refresh_forecast", NoArguments, default);

            Assert.True(result.IsSuccess);
            Assert.Equal("ok", result.Data!.Result.GetProperty("content")[0].GetProperty("text").GetString());
        }

        [Fact]
        public async Task AnApp_MayNotCall_AWrite_AToolNotForApps_OneTheReaderTurnedOff_OrAnyOnAServerWithoutApps()
        {
            var withApp = new CachedTool("forecast", null, "", ToolEffect.Read, Schema, AppUri);
            var id = AddInstallation("u1", Cached(withApp,
                new CachedTool("set_alert", null, "", ToolEffect.Write, Schema),
                new CachedTool("model_only", null, "", ToolEffect.Read, Schema, NotForApp: true)));
            var noApps = AddInstallation("u1", Cached(new CachedTool("forecast", null, "", ToolEffect.Read, Schema)));
            var off = AddInstallation("u1", Cached(withApp));
            using (var setup = new AzureSQLDBContext(_options))
            {
                setup.ConnectorInstallations.Single(c => c.Id == off).ToolSettingsJson = """{"forecast":"Disabled"}""";
                setup.SaveChanges();
            }
            var mcp = Calling("forecast");
            using var db = new AzureSQLDBContext(_options);
            var apps = Apps("u1", mcp.Object, db);

            Assert.Equal(ErrorCodes.Forbidden, (await apps.CallAsync(id, "set_alert", NoArguments, default)).Error);
            Assert.Equal(ErrorCodes.Forbidden, (await apps.CallAsync(id, "model_only", NoArguments, default)).Error);
            Assert.Equal(ErrorCodes.Forbidden, (await apps.CallAsync(id, "not_listed", NoArguments, default)).Error);
            Assert.Equal(ErrorCodes.Forbidden, (await apps.CallAsync(noApps, "forecast", NoArguments, default)).Error);
            Assert.Equal(ErrorCodes.Forbidden, (await apps.CallAsync(off, "forecast", NoArguments, default)).Error);
            Assert.Equal(ErrorCodes.NotFound, (await Apps("u2", mcp.Object, db).CallAsync(id, "forecast", NoArguments, default)).Error);
            mcp.Verify(m => m.CallToolAsync(It.IsAny<Uri>(), It.IsAny<McpAuth>(), It.IsAny<string>(), It.IsAny<JsonElement>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AToolsVisibilityWithoutApp_IsKeptAsNotForApps()
        {
            var listed = await Client(new FakeMcpServer
            {
                ToolsJson = """
                    { "tools": [ { "name": "a", "inputSchema": { "type": "object" }, "_meta": { "ui": { "visibility": ["model"] } } },
                                 { "name": "b", "inputSchema": { "type": "object" } } ] }
                    """,
            }).ListToolsAsync(Server, McpAuth.None, CancellationToken.None);

            Assert.True(listed.Tools[0].NotForApp);
            Assert.False(listed.Tools[1].NotForApp);
            Assert.True(CachedTools.Parse(CachedTools.Serialize(listed.Tools))[0].NotForApp);
        }

        [Fact]
        public void OnlyPlainHttpsOrigins_MayJoinTheCsp()
        {
            var kept = McpToolClient.HttpsOrigins(new JsonArray(
                "https://ok.example.com", "https://ok.example.com", "https://ok.example.com/", "HTTPS://Upper.Example.com",
                "https://user@x.example.com", "https://x.example.com?q=1", "data:", "'self'", "https://x.example.com;script-src *", 5,
                "https://192.168.1.1", "https://[::1]", "https://localhost", "https://intranet", "https://printer.local"));

            Assert.Equal(new[] { "https://ok.example.com", "https://upper.example.com" }, kept);
        }
    }
}
