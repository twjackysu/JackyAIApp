using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;
using SQLWordMeaning = JackyAIApp.Server.Data.Models.SQL.WordMeaning;
using SQLDefinition = JackyAIApp.Server.Data.Models.SQL.Definition;

namespace JackyAIApp.Server.Tests.Controllers
{
    public class DictionaryControllerTests : IDisposable
    {
        private readonly Mock<ILogger<DictionaryController>> _loggerMock;
        private readonly Mock<IMyResponseFactory> _responseFactoryMock;
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly Mock<IExtendedMemoryCache> _memoryCacheMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly DictionaryController _controller;

        public DictionaryControllerTests()
        {
            _loggerMock = new Mock<ILogger<DictionaryController>>();
            _responseFactoryMock = new Mock<IMyResponseFactory>();
            _promptServiceMock = new Mock<IOpenAIPromptService>();
            _memoryCacheMock = new Mock<IExtendedMemoryCache>();

            // Setup in-memory database
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            // Setup response factory
            _responseFactoryMock.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(new { Success = true, Data = data }));
            _responseFactoryMock.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new BadRequestObjectResult(new { Success = false, Code = code, Message = msg }));

            // Setup cache to always miss by default
            _memoryCacheMock.Setup(x => x.TryGetValue(It.IsAny<string>(), out It.Ref<object?>.IsAny))
                .Returns(false);

            _controller = new DictionaryController(
                _loggerMock.Object,
                _responseFactoryMock.Object,
                _dbContext,
                _promptServiceMock.Object,
                _memoryCacheMock.Object);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        #region Get Tests

        [Fact]
        public async Task Get_InvalidWord_ReturnsError()
        {
            // Arrange - "xyz123" is not a valid English word

            // Act
            var result = await _controller.Get("xyz123invalidword");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.TheWordCannotBeFound, 
                "This is not a valid word."), Times.Once);
        }

        [Fact]
        public async Task Get_WordExistsInDb_ReturnsFromDb()
        {
            // Arrange
            var word = await SeedWordAsync("hello");

            // Act
            var result = await _controller.Get("hello");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<DTO.Word>(w => w.Word == "hello")), Times.Once);
            
            // Should NOT call OpenAI since word exists
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Never);
        }

        [Fact]
        public async Task Get_WordNotInDb_GeneratesAndSaves()
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
            var result = await _controller.Get("test");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<DTO.Word>(w => w.Word == "test")), Times.Once);

            // Verify saved to DB
            var savedWord = await _dbContext.Words
                .Include(w => w.Meanings)
                .FirstOrDefaultAsync(w => w.WordText == "test");
            Assert.NotNull(savedWord);
            Assert.Single(savedWord.Meanings);
        }

        [Fact]
        public async Task Get_WordMarkedInvalid_RegeneratesFromOpenAI()
        {
            // Arrange
            var invalidWord = await SeedWordAsync("book", isInvalid: true);
            
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
            var result = await _controller.Get("book");

            // Assert
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), "book", It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Once);

            // Verify DataInvalid is cleared
            var updatedWord = await _dbContext.Words.FirstAsync(w => w.WordText == "book");
            Assert.Null(updatedWord.DataInvalid);
        }

        [Fact]
        public async Task Get_OpenAIFails_ReturnsError()
        {
            // Arrange
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync(((WordBase?)null, "OpenAI error"));

            // Act
            var result = await _controller.Get("test");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.QueryOpenAIFailed, 
                It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region Invalid Tests

        [Fact]
        public async Task Invalid_WordExists_MarksAsInvalid()
        {
            // Arrange
            var word = await SeedWordAsync("hello");

            // Act
            var result = await _controller.Invalid("hello");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<DTO.Word>(w => w.DataInvalid == true)), Times.Once);

            // Verify DB updated
            var updatedWord = await _dbContext.Words.FirstAsync(w => w.WordText == "hello");
            Assert.True(updatedWord.DataInvalid);
        }

        [Fact]
        public async Task Invalid_WordNotFound_ReturnsError()
        {
            // Act
            var result = await _controller.Invalid("nonexistent");

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.TheWordCannotBeFound, 
                "Word not found"), Times.Once);
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
