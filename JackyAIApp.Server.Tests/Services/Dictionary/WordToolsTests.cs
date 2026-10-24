using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Dictionary;
using JackyAIApp.Server.Services.Dictionary.Assistant;
using JackyAIApp.Server.Services.Repository;
using Moq;
using DTO = JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Tests.Services.Dictionary
{
    /// <summary>The English app's tools for Studio AI: a word lookup, and adding a word (asked first).</summary>
    public class WordToolsTests
    {
        private readonly Mock<IDictionaryService> _dictionary = new();
        private readonly Mock<IRepositoryService> _repository = new();
        private static readonly AiToolContext Context = new("u1", "c1");

        private static DTO.Word Entry(string word) => new()
        {
            Id = $"id-{word}",
            PartitionKey = word,
            Word = word,
            KKPhonics = "[͵sɛrənˋdɪpətɪ]",
            DateAdded = new DateTime(2026, 1, 1),
            SeoTitle = word,
            SeoDescription = word,
            Meanings =
            [
                new DTO.WordMeaning
                {
                    PartOfSpeech = "noun",
                    Definitions = [new DTO.Definition { English = "luck in finding good things", Chinese = "意外發現美好事物的運氣" }],
                    ExampleSentences = [new DTO.ExampleSentence { English = "It was pure serendipity.", Chinese = "純屬巧合。" }],
                    Synonyms = ["chance"],
                    Antonyms = [],
                    RelatedWords = [],
                },
            ],
        };

        private void DictionaryHas(string word) =>
            _dictionary.Setup(d => d.GetWordAsync(word, It.IsAny<CancellationToken>())).ReturnsAsync(ServiceResult.Ok(Entry(word)));

        private static JsonElement Word(string word) => JsonSerializer.SerializeToElement(new { word });

        [Fact]
        public void TheTools_ReadAndWrite_AsTheirNamesSay()
        {
            Assert.Equal(ToolEffect.Read, LookUpWordTool.Definition.Effect);
            Assert.Equal(ToolEffect.Write, AddWordToRepositoryTool.Definition.Effect);
            Assert.Equal(AiToolDescriptor.SiteConnector, AddWordToRepositoryTool.Definition.Connector);
        }

        [Fact]
        public async Task ALookup_GivesTheModelTheMeanings_AndTheReaderALinkToTheWordsPage()
        {
            DictionaryHas("serendipity");

            var result = await new LookUpWordTool(_dictionary.Object).RunAsync(Word("Serendipity"), Context, default);

            Assert.False(result.IsError);
            Assert.Contains("意外發現美好事物的運氣", result.Content);
            Assert.Contains("It was pure serendipity.", result.Content);
            var card = JsonSerializer.SerializeToElement(result.Card);
            Assert.Equal("/english/dictionary/serendipity", card.GetProperty("href").GetString());
        }

        [Theory]
        [InlineData("../admin")]
        [InlineData("")]
        [InlineData("字典")]
        public async Task AWordThatIsNotPlain_IsNotLookedUp(string word)
        {
            var result = await new LookUpWordTool(_dictionary.Object).RunAsync(Word(word), Context, default);

            Assert.True(result.IsError);
            _dictionary.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AWordTheDictionaryDoesNotHave_IsSaidSo()
        {
            _dictionary.Setup(d => d.GetWordAsync("qwzx", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.BadRequest, "invalid"));

            var result = await new LookUpWordTool(_dictionary.Object).RunAsync(Word("qwzx"), Context, default);

            Assert.True(result.IsError);
            Assert.Contains("找不到", result.Content);
        }

        [Fact]
        public async Task AddingAWord_AddsItsEntryToTheRepository()
        {
            DictionaryHas("serendipity");
            _repository.Setup(r => r.AddWordAsync("id-serendipity")).ReturnsAsync(ServiceResult.Ok());

            var result = await new AddWordToRepositoryTool(_dictionary.Object, _repository.Object).RunAsync(Word("serendipity"), Context, default);

            Assert.False(result.IsError);
            Assert.Contains("已把「serendipity」加入", result.Content);
            _repository.Verify(r => r.AddWordAsync("id-serendipity"), Times.Once);
        }

        [Fact]
        public async Task AWordAlreadyThere_IsNotAFailure()
        {
            DictionaryHas("serendipity");
            _repository.Setup(r => r.AddWordAsync("id-serendipity"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.BadRequest, "The word has already been added."));

            var result = await new AddWordToRepositoryTool(_dictionary.Object, _repository.Object).RunAsync(Word("serendipity"), Context, default);

            Assert.False(result.IsError);
            Assert.Contains("本來就在", result.Content);
        }
    }
}
