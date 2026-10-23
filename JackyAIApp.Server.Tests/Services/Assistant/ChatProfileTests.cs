using System.Text.Json;
using JackyAIApp.Server.Services.Assistant;
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
                new[] { "cache_control", "max_tokens", "messages", "model", "output_config", "system" },
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

            var setup = await finance.SetupAsync(Today, CancellationToken.None);

            Assert.Equal(new ChatProfileSetup(FinanceChatProfile.SystemPromptFor(Today), "# 市場背景", AttachTwStock: true), setup);
            Assert.Equal("chatbot.tw_stock", finance.UsageLogFeature);
        }

        [Fact]
        public async Task Finance_GoesAheadWithoutTheBackground_WhenItCannotBeHad()
        {
            var background = new Mock<IMarketBackground>();
            background.Setup(b => b.GetAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("TWSE down"));
            var finance = new FinanceChatProfile(background.Object, NullLogger<FinanceChatProfile>.Instance);

            var setup = await finance.SetupAsync(Today, CancellationToken.None);

            Assert.Null(setup.CachedBackground);
            Assert.True(setup.AttachTwStock);
        }

        /// <summary>The request body as it goes over the wire.</summary>
        private static JsonElement Body(Anthropic.Models.Beta.Messages.MessageCreateParams request) =>
            JsonSerializer.SerializeToElement(request).GetProperty("RawBodyData");
    }
}
