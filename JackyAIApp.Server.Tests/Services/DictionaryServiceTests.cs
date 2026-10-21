using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpellChecker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;
using SQLWordMeaning = JackyAIApp.Server.Data.Models.SQL.WordMeaning;
using SQLDefinition = JackyAIApp.Server.Data.Models.SQL.Definition;

namespace JackyAIApp.Server.Tests.Services
{
    public class DictionaryServiceTests : IDisposable
    {
        private readonly Mock<ILogger<DictionaryService>> _loggerMock;
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly Mock<IExtendedMemoryCache> _memoryCacheMock;
        private readonly Mock<ISpellChecker> _spellCheckerMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly DictionaryService _service;

        public DictionaryServiceTests()
        {
            _loggerMock = new Mock<ILogger<DictionaryService>>();
            _promptServiceMock = new Mock<IOpenAIPromptService>();
            _memoryCacheMock = new Mock<IExtendedMemoryCache>();
            _spellCheckerMock = new Mock<ISpellChecker>();

            // By default treat words as valid; invalid-word tests override per word.
            _spellCheckerMock.Setup(x => x.Check(It.IsAny<string>())).Returns(true);

            // Setup in-memory database
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            // Setup cache to always miss by default
            _memoryCacheMock.Setup(x => x.TryGetValue(It.IsAny<string>(), out It.Ref<object?>.IsAny))
                .Returns(false);

            _service = new DictionaryService(
                _loggerMock.Object,
                _dbContext,
                _promptServiceMock.Object,
                _memoryCacheMock.Object,
                _spellCheckerMock.Object);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        #region GetWord Tests

        [Fact]
        public async Task GetWord_InvalidWord_ReturnsError()
        {
            // Arrange
            _spellCheckerMock.Setup(x => x.Check("xyz123invalidword")).Returns(false);

            // Act
            var result = await _service.GetWordAsync("xyz123invalidword");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.TheWordCannotBeFound, result.Error);
            Assert.Equal("This is not a valid word.", result.ErrorMessage);
        }

        [Fact]
        public async Task GetWord_WordExistsInDb_ReturnsFromDb()
        {
            // Arrange
            await SeedWordAsync("hello");

            // Act
            var result = await _service.GetWordAsync("hello");

            // Assert
            Assert.True(result.IsSuccess);
            var dto = (DTO.Word)result.Data!;
            Assert.Equal("hello", dto.Word);
            Assert.Equal("hello 英文意思、發音、例句 | English Coach", dto.SeoTitle);
            Assert.StartsWith("hello (noun):", dto.SeoDescription);

            // Should NOT call OpenAI since word exists
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Never);
        }

        [Fact]
        public async Task GetWord_WordNotInDb_GeneratesAndSaves()
        {
            // Arrange
            var generatedWord = new WordBase
            {
                Word = "test",
                KKPhonics = "/test/",
                Meanings = new List<DTO.WordMeaning>
                {
                    new()
                    {
                        PartOfSpeech = "noun",
                        Definitions = new List<DTO.Definition>
                        {
                            new() { English = "A procedure to check quality", Chinese = "檢驗程序" }
                        },
                        ExampleSentences = new List<DTO.ExampleSentence>
                        {
                            new() { English = "This is a test.", Chinese = "這是一個測試。" }
                        },
                        Synonyms = new List<string> { "exam" },
                        Antonyms = new List<string>(),
                        RelatedWords = new List<string> { "quiz" }
                    }
                }
            };

            _promptServiceMock.Setup(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((generatedWord, (string?)null));

            // Act
            var result = await _service.GetWordAsync("test");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("test", ((DTO.Word)result.Data!).Word);

            // Verify saved to DB
            var savedWord = await _dbContext.Words
                .Include(w => w.Meanings)
                .FirstOrDefaultAsync(w => w.WordText == "test");
            Assert.NotNull(savedWord);
            Assert.Single(savedWord.Meanings);
        }

        [Fact]
        public async Task GetWord_WordMarkedInvalid_RegeneratesFromOpenAI()
        {
            // Arrange
            await SeedWordAsync("book", isInvalid: true);

            var generatedWord = new WordBase
            {
                Word = "book",
                KKPhonics = "/bʊk/",
                Meanings = new List<DTO.WordMeaning>
                {
                    new()
                    {
                        PartOfSpeech = "noun",
                        Definitions = new List<DTO.Definition>
                        {
                            new() { English = "A written work", Chinese = "書本" }
                        },
                        ExampleSentences = new List<DTO.ExampleSentence>(),
                        Synonyms = new List<string>(),
                        Antonyms = new List<string>(),
                        RelatedWords = new List<string>()
                    }
                }
            };

            _promptServiceMock.Setup(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((generatedWord, (string?)null));

            // Act
            var result = await _service.GetWordAsync("book");

            // Assert
            Assert.True(result.IsSuccess);
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), "book", It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Once);

            // Verify DataInvalid is cleared
            var updatedWord = await _dbContext.Words.FirstAsync(w => w.WordText == "book");
            Assert.Null(updatedWord.DataInvalid);
        }

        [Fact]
        public async Task GetWord_OpenAIFails_ReturnsError()
        {
            // Arrange
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync(((WordBase?)null, "OpenAI error"));

            // Act
            var result = await _service.GetWordAsync("test");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        #endregion

        #region InvalidateWord Tests

        [Fact]
        public async Task InvalidateWord_WordExists_MarksAsInvalid()
        {
            // Arrange
            await SeedWordAsync("hello");

            // Act
            var result = await _service.InvalidateWordAsync("hello");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(((DTO.Word)result.Data!).DataInvalid);

            // Verify DB updated
            var updatedWord = await _dbContext.Words.FirstAsync(w => w.WordText == "hello");
            Assert.True(updatedWord.DataInvalid);
        }

        [Fact]
        public async Task InvalidateWord_WordNotFound_ReturnsError()
        {
            // Act
            var result = await _service.InvalidateWordAsync("nonexistent");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.TheWordCannotBeFound, result.Error);
            Assert.Equal("Word not found", result.ErrorMessage);
        }

        #endregion

        #region SuggestWords Tests

        [Fact]
        public void SuggestWords_MisspelledWord_ReturnsSpellCheckerSuggestions()
        {
            // Arrange
            _spellCheckerMock.Setup(x => x.Suggest("aple")).Returns(new List<string> { "apple", "ample" });

            // Act
            var result = _service.SuggestWords("aple");

            // Assert
            Assert.True(result.IsSuccess);
            var suggestions = result.Data!;
            Assert.Equal(new[] { "apple", "ample" }, suggestions.Suggestions);
        }

        [Fact]
        public void SuggestWords_EmptyWord_ReturnsEmptyWithoutCallingSpellChecker()
        {
            // Act
            var result = _service.SuggestWords("   ");

            // Assert
            Assert.True(result.IsSuccess);
            var suggestions = result.Data!;
            Assert.Empty(suggestions.Suggestions);
            _spellCheckerMock.Verify(x => x.Suggest(It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region Helper Methods

        private async Task<SQLWord> SeedWordAsync(string wordText, bool isInvalid = false)
        {
            var wordId = Guid.NewGuid().ToString();
            var meaningId = Guid.NewGuid().ToString();
            var definitionId = Guid.NewGuid().ToString();

            var word = new SQLWord
            {
                Id = wordId,
                WordText = wordText,
                KKPhonics = $"/{wordText}/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                DataInvalid = isInvalid ? true : null,
                Meanings = new List<SQLWordMeaning>()
            };

            var meaning = new SQLWordMeaning
            {
                Id = meaningId,
                PartOfSpeech = "noun",
                WordId = wordId,
                Word = word,
                Definitions = new List<SQLDefinition>(),
                ExampleSentences = new List<Data.Models.SQL.ExampleSentence>(),
                Tags = new List<WordMeaningTag>()
            };

            var definition = new SQLDefinition
            {
                Id = definitionId,
                English = $"Definition of {wordText}",
                Chinese = $"{wordText}的定義",
                WordMeaningId = meaningId,
                WordMeaning = meaning
            };

            meaning.Definitions.Add(definition);
            word.Meanings.Add(meaning);

            _dbContext.Words.Add(word);
            await _dbContext.SaveChangesAsync();
            return word;
        }

        #endregion
    }
}
