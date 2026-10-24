using System.Text.Json;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Assistant.Studio;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Connectors.Google;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using JackyAIApp.Server.Services.Portfolio.Assistant;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Assistant
{
    /// <summary>Studio AI: the site's pages, where the reader is, and the reader's connectors.</summary>
    public class StudioChatProfileTests
    {
        private static readonly DateOnly Today = new(2026, 10, 24);
        private readonly List<IAiTool> _connectorTools = [];
        private readonly List<ISiteTool> _siteTools = [];

        private async Task<ChatProfileSetup> SetupAsync(string? path = null)
        {
            var connectors = new Mock<IConnectorToolSource>();
            connectors.Setup(c => c.ForUserAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(() => _connectorTools.ToList());
            var page = path == null ? new Dictionary<string, string>() : new Dictionary<string, string> { [StudioChatProfile.PathKey] = path };
            return await new StudioChatProfile(_siteTools, connectors.Object).SetupAsync(new ChatProfileContext("u1", Today, page), default);
        }

        private static ChatProfileContext At(string path) =>
            new("u1", Today, new Dictionary<string, string> { [StudioChatProfile.PathKey] = path });

        [Fact]
        public async Task ItKnowsTheSitesPages_AndHasItsLinkTool_AndNoTwStock()
        {
            var setup = await SetupAsync();

            Assert.Contains("2026-10-24", setup.SystemPrompt);
            Assert.Contains("/data-analysis", setup.SystemPrompt);
            Assert.Contains("讓其他 AI 使用本站", setup.SystemPrompt);
            Assert.False(setup.AttachTwStock);
            Assert.Equal(OpenPageTool.Name, Assert.Single(setup.Tools!).Descriptor.Name);
        }

        [Fact]
        public async Task TheMorningBriefing_CallsTheThreeToolsByTheirOwnNames()
        {
            var setup = await SetupAsync();

            // The home page's 「AI 今日簡報」 sends 「給我今天的簡報」 (client: askStudio's BRIEFING_PROMPT).
            Assert.Contains("給我今天的簡報", setup.SystemPrompt);
            Assert.Contains(GoogleCalendarTool.Name, setup.SystemPrompt);
            Assert.Contains(LearningProgressTool.Name, setup.SystemPrompt);
            Assert.Contains(PortfolioSummaryTool.Name, setup.SystemPrompt);
        }

        [Theory]
        [InlineData("/english/dictionary/apple", "dictionary")]
        [InlineData("/english", "english")]
        [InlineData("/portfolio", "portfolio")]
        [InlineData("/", "home")]
        [InlineData("/nowhere", null)]
        [InlineData("https://evil.example/", null)]
        public void ThePageTheReaderIsOn_IsThePathsLongestPage(string path, string? key)
        {
            Assert.Equal(key, StudioChatProfile.PageOf(At(path))?.Key);
        }

        [Fact]
        public async Task ThePromptSaysWhereTheReaderIs()
        {
            var setup = await SetupAsync("/english/repository");

            Assert.Contains("使用者現在在「我的單字庫」", setup.SystemPrompt);
        }

        [Fact]
        public async Task TheFeaturesTools_ComeAfterOpenPage()
        {
            var lookUp = new Mock<ISiteTool>();
            lookUp.Setup(t => t.Descriptor).Returns(new AiToolDescriptor("look_up_word", "查字典", "說明",
                JsonSerializer.SerializeToElement(new { type = "object" }), ToolEffect.Read));
            _siteTools.Add(lookUp.Object);

            var setup = await SetupAsync();

            Assert.Equal(new[] { OpenPageTool.Name, "look_up_word" }, setup.Tools!.Select(t => t.Descriptor.Name));
        }

        [Fact]
        public async Task TheReadersConnectors_ComeAfterTheSitesTool()
        {
            var outside = new Mock<IAiTool>();
            outside.Setup(t => t.Descriptor).Returns(new AiToolDescriptor("m1__search", "Notion・搜尋", "說明",
                JsonSerializer.SerializeToElement(new { type = "object" }), ToolEffect.Read, "inst-1", "Notion"));
            _connectorTools.Add(outside.Object);

            var setup = await SetupAsync();

            Assert.Equal(new[] { OpenPageTool.Name, "m1__search" }, setup.Tools!.Select(t => t.Descriptor.Name));
            Assert.Contains("外部 connector", setup.SystemPrompt);
        }

        private static async Task<AiToolResult> OpenAsync(object input) =>
            await new OpenPageTool().RunAsync(JsonSerializer.SerializeToElement(input), new AiToolContext("u1", "c1"), default);

        [Fact]
        public async Task OpenPage_GivesALinkCardToThePage()
        {
            var result = await OpenAsync(new { page = "portfolio" });

            Assert.False(result.IsError);
            var card = JsonSerializer.SerializeToElement(result.Card);
            Assert.Equal("link", card.GetProperty("kind").GetString());
            Assert.Equal("/portfolio", card.GetProperty("href").GetString());
        }

        [Theory]
        [InlineData("Serendipity", "/english/dictionary/serendipity")]
        [InlineData("ice cream", "/english/dictionary/ice%20cream")]
        [InlineData("../admin", "/english/dictionary")]
        [InlineData("<script>", "/english/dictionary")]
        public async Task OpenPage_TakesTheDictionaryToAWord_OnlyAPlainOne(string word, string href)
        {
            var result = await OpenAsync(new { page = "dictionary", word });

            Assert.Equal(href, JsonSerializer.SerializeToElement(result.Card).GetProperty("href").GetString());
        }

        [Fact]
        public async Task OpenPage_RefusesAPageThatIsNotTheSites()
        {
            var result = await OpenAsync(new { page = "https://evil.example" });

            Assert.True(result.IsError);
            Assert.Null(result.Card);
        }
    }
}
