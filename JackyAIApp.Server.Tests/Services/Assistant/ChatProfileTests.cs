using System.Text.Json;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Finance.Assistant;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Assistant
{
    /// <summary>The shared chat engine's profiles, and the request a profile without tw-stock sends.</summary>
    public class ChatProfileTests
    {
        private static readonly DateOnly Today = new(2026, 10, 21);

        [Fact]
        public void AProfileWithoutTwStock_SendsNoMcpServerNoToolsAndNoBeta()
        {
            var request = ChatTurnRequest.Build(new ChatProfileSetup("你是助手。", null, AttachTwStock: false), []);
            var body = Body(request);

            Assert.Equal(
                new[] { "cache_control", "max_tokens", "messages", "model", "output_config", "system", "thinking" },
                body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal("你是助手。", body.GetProperty("system").GetString());
            Assert.DoesNotContain("mcp-client", JsonSerializer.Serialize(request));
        }

        [Fact]
        public void AProfileWithoutTwStock_StillSendsItsBackgroundAsASecondCachedBlock()
        {
            var system = Body(ChatTurnRequest.Build(new ChatProfileSetup("你是助手。", "# 資料源", AttachTwStock: false), []))
                .GetProperty("system");

            Assert.Equal(2, system.GetArrayLength());
            Assert.Equal("# 資料源", system[1].GetProperty("text").GetString());
            Assert.Equal("ephemeral", system[1].GetProperty("cache_control").GetProperty("type").GetString());
        }

        private sealed class NamedTool(string name) : IAiTool
        {
            public AiToolDescriptor Descriptor { get; } = new(name, name, $"{name} 的說明",
                JsonSerializer.SerializeToElement(new { type = "object", properties = new { q = new { type = "string" } }, required = new[] { "q" } }),
                ToolEffect.Read);

            public Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct) =>
                Task.FromResult(new AiToolResult("ok"));
        }

        [Fact]
        public void AProfileWithNoToolsOfItsOwn_SendsTheSameRequestAsBefore()
        {
            var without = JsonSerializer.Serialize(ChatTurnRequest.Build(new ChatProfileSetup("你是助手。", null, AttachTwStock: true), []));
            var empty = JsonSerializer.Serialize(ChatTurnRequest.Build(new ChatProfileSetup("你是助手。", null, AttachTwStock: true, Tools: []), []));

            Assert.Equal(without, empty);
        }

        [Fact]
        public void ToolsOfItsOwn_GoSortedByName_WithTheirSchema_AndThePromptSaysTheirResultsAreData()
        {
            var setup = new ChatProfileSetup("你是助手。", null, AttachTwStock: false, Tools: [new NamedTool("b_tool"), new NamedTool("a_tool")]);
            var body = Body(ChatTurnRequest.Build(setup, []));

            var tools = body.GetProperty("tools");
            Assert.Equal(new[] { "a_tool", "b_tool" }, tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
            Assert.Equal("a_tool 的說明", tools[0].GetProperty("description").GetString());
            Assert.Equal("object", tools[0].GetProperty("input_schema").GetProperty("type").GetString());
            Assert.Equal("q", tools[0].GetProperty("input_schema").GetProperty("required")[0].GetString());
            Assert.Equal("你是助手。" + ChatTurnRequest.UntrustedToolOutputInstruction, body.GetProperty("system").GetString());
            Assert.False(body.TryGetProperty("mcp_servers", out _));
        }

        [Fact]
        public void ToolsOfItsOwn_WithTwStock_FollowTheTwStockTools()
        {
            var setup = new ChatProfileSetup("你是助手。", null, AttachTwStock: true, Tools: [new NamedTool("a_tool")]);
            var body = Body(ChatTurnRequest.Build(setup, []));

            Assert.Equal(new[] { "tool_search_tool_regex_20251119", "mcp_toolset", null },
                body.GetProperty("tools").EnumerateArray()
                    .Select(t => t.TryGetProperty("type", out var type) ? type.GetString() : null));
            Assert.Equal("a_tool", body.GetProperty("tools")[2].GetProperty("name").GetString());
            Assert.Equal(1, body.GetProperty("mcp_servers").GetArrayLength());
        }

        [Fact]
        public void Profiles_AreFoundByTheirKey_AndAnUnknownKeyIsNone()
        {
            var finance = new FinanceChatProfile(Mock.Of<IMarketBackground>(), NullLogger<FinanceChatProfile>.Instance);
            var profiles = new ChatProfiles([finance]);

            Assert.Same(finance, profiles.Find("finance"));
            Assert.Null(profiles.Find("Finance"));
            Assert.Null(profiles.Find("data-analysis"));
        }

        [Fact]
        public async Task Finance_SetsUpWithItsPromptTwStockAndTheMarketBackground()
        {
            var background = new Mock<IMarketBackground>();
            background.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("# 市場背景");
            var finance = new FinanceChatProfile(background.Object, NullLogger<FinanceChatProfile>.Instance);

            var setup = await finance.SetupAsync(new ChatProfileContext("u1", Today, new Dictionary<string, string>()), CancellationToken.None);

            Assert.Equal(new ChatProfileSetup(FinanceChatProfile.SystemPromptFor(Today), "# 市場背景", AttachTwStock: true), setup);
            Assert.Equal("chatbot.tw_stock", finance.UsageLogFeature);
        }

        [Fact]
        public async Task Finance_GoesAheadWithoutTheBackground_WhenItCannotBeHad()
        {
            var background = new Mock<IMarketBackground>();
            background.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("TWSE down"));
            var finance = new FinanceChatProfile(background.Object, NullLogger<FinanceChatProfile>.Instance);

            var setup = await finance.SetupAsync(new ChatProfileContext("u1", Today, new Dictionary<string, string>()), CancellationToken.None);

            Assert.Null(setup.CachedBackground);
            Assert.True(setup.AttachTwStock);
        }

        /// <summary>The request body as it goes over the wire.</summary>
        private static JsonElement Body(Anthropic.Models.Beta.Messages.MessageCreateParams request) =>
            JsonSerializer.SerializeToElement(request).GetProperty("RawBodyData");
    }
}
