using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using JackyAIApp.Server.Services.Repository;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Dictionary
{
    /// <summary>Studio AI's view of the reader's own words: what the model is told, and the card.</summary>
    public class SavedWordsToolTests
    {
        private static readonly AiToolContext Context = new("u1", "c1");

        private readonly Mock<IRepositoryService> _repository = new();

        private static readonly SavedWords Three = new(42,
        [
            new SavedWord("serendipity", "noun", "意外發現美好事物的運氣"),
            new SavedWord("ephemeral", "adjective", "短暫的"),
            new SavedWord("lucid", null, null),
        ]);

        private Task<AiToolResult> RunAsync(object input) =>
            new SavedWordsTool(_repository.Object).RunAsync(JsonSerializer.SerializeToElement(input), Context, default);

        private static JsonElement Card(AiToolResult result) => JsonSerializer.SerializeToElement(result.Card);

        [Fact]
        public async Task ListsTheWords_WithTheirMeanings_AndTheTotal()
        {
            _repository.Setup(r => r.GetRecentWordsAsync(SavedWordsTool.DefaultCount, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(Three));

            var result = await RunAsync(new { });

            Assert.False(result.IsError);
            Assert.Contains("共 42 個字", result.Content);
            Assert.Contains("- serendipity (noun) 意外發現美好事物的運氣", result.Content);
            Assert.Contains("- ephemeral (adjective) 短暫的", result.Content);
            Assert.Contains("- lucid", result.Content);
            Assert.DoesNotContain("lucid (", result.Content);
            Assert.Contains("粗體", result.Content);
        }

        [Fact]
        public async Task PassesTheCountAsked()
        {
            _repository.Setup(r => r.GetRecentWordsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(Three));

            var result = await RunAsync(new { count = 5 });

            Assert.False(result.IsError);
            _repository.Verify(r => r.GetRecentWordsAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ItsCard_LeadsToTheRepository()
        {
            _repository.Setup(r => r.GetRecentWordsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(Three));

            var card = Card(await RunAsync(new { }));

            Assert.Equal("link", card.GetProperty("kind").GetString());
            Assert.Equal("/english/repository", card.GetProperty("href").GetString());
            Assert.Contains("共 42 個字", card.GetProperty("description").GetString());
        }

        [Fact]
        public async Task AnEmptyCollection_SuggestsLookingWordsUp()
        {
            _repository.Setup(r => r.GetRecentWordsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Ok(new SavedWords(0, [])));

            var result = await RunAsync(new { });

            Assert.False(result.IsError);
            Assert.Contains("還是空的", result.Content);
            Assert.Equal("/english/dictionary", Card(result).GetProperty("href").GetString());
        }

        [Fact]
        public async Task AFailedRead_IsAnError()
        {
            _repository.Setup(r => r.GetRecentWordsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.InternalServerError, "boom"));

            var result = await RunAsync(new { });

            Assert.True(result.IsError);
            Assert.DoesNotContain("boom", result.Content);
        }

        [Fact]
        public void ItIsARead()
        {
            Assert.Equal(ToolEffect.Read, SavedWordsTool.Definition.Effect);
        }
    }
}
