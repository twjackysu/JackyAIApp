using System.Text.Json;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace JackyAIApp.Server.Tests.Services.Connectors
{
    /// <summary>
    /// The MCP client against a minimal Streamable HTTP server (<see cref="FakeMcpServer"/>): the real SDK
    /// does the protocol, the fake plays the server's side of it.
    /// </summary>
    public class McpToolClientTests
    {
        private static readonly Uri Server = new("https://mcp.example.com/mcp");

        private static McpToolClient Client(FakeMcpServer server) => new(server, NullLoggerFactory.Instance);

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
        public async Task ARefusalThatNamesTheScopeACallNeeds_SaysWhichScope()
        {
            var server = new FakeMcpServer { CallsNeedScope = "notes:write" };

            var ex = await Assert.ThrowsAsync<McpServerException>(() => Client(server).CallToolAsync(Server, McpAuth.None, "add_note",
                JsonSerializer.SerializeToElement(new { }), CancellationToken.None));

            Assert.Equal((McpFailure.NeedsAuth, "notes:write"), (ex.Failure, ex.RequiredScope));
        }

        [Fact]
        public void OnlyAnInsufficientScopeChallenge_NamesAScope()
        {
            var headers = new HttpResponseMessage().Headers.WwwAuthenticate;
            headers.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "error=\"invalid_token\", scope=\"a\""));
            Assert.Null(McpToolClient.InsufficientScopeOf(headers));

            headers.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "error=\"insufficient_scope\", scope=\" a b \""));
            Assert.Equal("a b", McpToolClient.InsufficientScopeOf(headers));
        }

        [Fact]
        public void AnAddressThatIsNotPublic_IsBlocked_InTheReadersWords()
        {
            var ex = McpToolClient.Classify(new HttpRequestException("x", new BlockedAddressException("10.0.0.1")), timedOut: false);

            Assert.Equal(McpFailure.Blocked, ex.Failure);
            Assert.DoesNotContain("10.0.0.1", ex.Message);
        }

        [Fact]
        public async Task AnAnswerTooLargeToHold_IsAFailure_NotReadIntoMemory()
        {
            var huge = new string('a', (int)McpToolClient.MaxResponseBytes + 1);
            var server = new FakeMcpServer
            {
                ToolsJson = JsonSerializer.Serialize(new { tools = new[] { new { name = "big", description = huge, inputSchema = new { type = "object" } } } }),
            };

            var ex = await Assert.ThrowsAsync<McpServerException>(() => Client(server).ListToolsAsync(Server, McpAuth.None, CancellationToken.None));

            Assert.Equal(McpFailure.Unreachable, ex.Failure);
        }

        [Fact]
        public void AnAnswerTooLarge_IsSaidSo()
        {
            var ex = McpToolClient.Classify(new HttpRequestException("x", new ResponseTooLargeException(10)), timedOut: false);

            Assert.Equal(McpFailure.Unreachable, ex.Failure);
            Assert.Contains("太大", ex.Message);
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
