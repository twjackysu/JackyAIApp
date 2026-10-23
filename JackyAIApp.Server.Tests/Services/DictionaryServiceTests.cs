using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpellChecker;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;
using SQLWordMeaning = JackyAIApp.Server.Data.Models.SQL.WordMeaning;
using SQLDefinition = JackyAIApp.Server.Data.Models.SQL.Definition;

namespace JackyAIApp.Server.Tests.Services
{
    public class DictionaryServiceTests : IDisposable
    {
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
        private readonly Mock<ISpellChecker> _spellCheckerMock;
        private readonly string _dbName = Guid.NewGuid().ToString();
        // 01:00 in Taipei, still the day before in UTC: a stored time that is Taipei's would show here.
        private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero));
        private readonly InMemoryDatabaseRoot _dbRoot = new();
        private readonly ServiceProvider _provider;
        private readonly AzureSQLDBContext _dbContext;
        private readonly DictionaryService _service;

        public DictionaryServiceTests()
        {
            _promptServiceMock = new Mock<IOpenAIPromptService>();
            _spellCheckerMock = new Mock<ISpellChecker>();

            // By default treat words as valid, and as base words (no root); tests override per word.
            _spellCheckerMock.Setup(x => x.Check(It.IsAny<string>())).Returns(true);

            // The generator runs in scopes of its own; they share the in-memory database with the
            // request's context below.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AzureSQLDBContext>(o => o.UseInMemoryDatabase(_dbName, _dbRoot));
            services.AddSingleton(_promptServiceMock.Object);
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IWordGenerator, WordGenerator>();
            _provider = services.BuildServiceProvider();

            _dbContext = NewContext();
            _service = NewService(_dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _provider.Dispose();
            _memoryCache.Dispose();
        }

        private AzureSQLDBContext NewContext() =>
            new(new DbContextOptionsBuilder<AzureSQLDBContext>().UseInMemoryDatabase(_dbName, _dbRoot).Options);

        // Another request: its own context, the app's one generator and cache.
        private DictionaryService NewService(AzureSQLDBContext db) =>
            new(db, _provider.GetRequiredService<IWordGenerator>(), _memoryCache, _spellCheckerMock.Object, _clock);

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
            Assert.Null(dto.InflectedForm);

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

            // Verify DataInvalid is cleared (read afresh: this context still tracks the seeded row)
            await using var db = NewContext();
            var updatedWord = await db.Words.FirstAsync(w => w.WordText == "book");
            Assert.Null(updatedWord.DataInvalid);
            Assert.Equal(_clock.Now.UtcDateTime, updatedWord.LastUpdated);
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

        [Fact]
        public async Task GetWord_CallerStopsWaiting_TheWordIsStillStoredAndNotGeneratedAgain()
        {
            // The word page stops waiting after a few seconds and its request ends, while OpenAI is
            // still answering.
            var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var answer = new TaskCompletionSource<(WordBase?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetupPrompt().Callback(() => asked.TrySetResult()).Returns(answer.Task);

            using var stopWaiting = new CancellationTokenSource();
            var lookup = _service.GetWordAsync("test", stopWaiting.Token);
            await asked.Task;
            stopWaiting.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
            _dbContext.Dispose();

            answer.SetResult((Generated("test"), null));
            // Joins the run if it is still going; otherwise finds what it stored.
            await _provider.GetRequiredService<IWordGenerator>().GenerateAsync("test", CancellationToken.None);

            await using (var db = NewContext())
                Assert.True(await db.Words.AnyAsync(w => w.WordText == "test"));

            // The next visit reads it from the database.
            await using var nextRequest = NewContext();
            var result = await NewService(nextRequest).GetWordAsync("test");
            Assert.True(result.IsSuccess);
            VerifyPromptCalled(Times.Once());
        }

        [Fact]
        public async Task GetWord_LookupsOfAWordBeingGenerated_ShareOneGeneration()
        {
            var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var answer = new TaskCompletionSource<(WordBase?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetupPrompt().Callback(() => asked.TrySetResult()).Returns(answer.Task);

            var first = _service.GetWordAsync("test");
            await asked.Task;
            await using var otherRequest = NewContext();
            var second = NewService(otherRequest).GetWordAsync("test");
            // Time for the second lookup to reach OpenAI too, were it to generate on its own.
            await Task.Delay(200);
            answer.SetResult((Generated("test"), null));

            Assert.True((await first).IsSuccess);
            Assert.True((await second).IsSuccess);
            VerifyPromptCalled(Times.Once());
        }

        [Fact]
        public async Task GetWord_RegularInflection_ShowsTheRootsEntry()
        {
            _spellCheckerMock.Setup(x => x.RootOf("establishing")).Returns("establish");
            await SeedWordAsync("establish");

            var result = await _service.GetWordAsync("establishing");

            Assert.True(result.IsSuccess);
            Assert.Equal("establish", result.Data!.Word);
            Assert.Equal("establishing", result.Data.InflectedForm);
            VerifyPromptCalled(Times.Never());
        }

        [Fact]
        public async Task GetWord_RegularInflectionOfAWordNotStoredYet_GeneratesAndStoresTheRoot()
        {
            _spellCheckerMock.Setup(x => x.RootOf("establishing")).Returns("establish");
            SetupPrompt().ReturnsAsync((Generated("establish"), (string?)null));

            var result = await _service.GetWordAsync("establishing");

            Assert.True(result.IsSuccess);
            Assert.Equal("establish", result.Data!.Word);
            Assert.Equal("establishing", result.Data.InflectedForm);
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), "establish", It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Once);
            await using var db = NewContext();
            Assert.True(await db.Words.AnyAsync(w => w.WordText == "establish"));
            Assert.False(await db.Words.AnyAsync(w => w.WordText == "establishing"));
        }

        [Fact]
        public async Task GetWord_ModelAnswersWithAnotherWord_StoredUnderTheWordLookedUp()
        {
            // An irregular form keeps its own entry, but asked for "went" the model may define "go".
            SetupPrompt().ReturnsAsync((Generated("go"), (string?)null));

            var result = await _service.GetWordAsync("went");

            Assert.True(result.IsSuccess);
            await using (var db = NewContext())
                Assert.True(await db.Words.AnyAsync(w => w.WordText == "went"));

            // So the next lookup of it finds it instead of paying again.
            await using var nextRequest = NewContext();
            Assert.True((await NewService(nextRequest).GetWordAsync("went")).IsSuccess);
            VerifyPromptCalled(Times.Once());
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
            Assert.Equal(_clock.Now.UtcDateTime, updatedWord.LastUpdated);
        }

        [Fact]
        public async Task AGeneratedWord_IsStampedInUtc()
        {
            SetupPrompt().ReturnsAsync((Generated("apple"), null));

            await _service.GetWordAsync("apple");

            await using var db = NewContext();
            var stored = await db.Words.SingleAsync(w => w.WordText == "apple");
            Assert.Equal((_clock.Now.UtcDateTime, _clock.Now.UtcDateTime), (stored.DateAdded, stored.LastUpdated));
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

        private Moq.Language.Flow.ISetup<IOpenAIPromptService, Task<(WordBase? Result, string? Error)>> SetupPrompt() =>
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()));

        private void VerifyPromptCalled(Times times) =>
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WordBase>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), times);

        private static WordBase Generated(string word) => new()
        {
            Word = word,
            KKPhonics = $"/{word}/",
            Meanings = new List<DTO.WordMeaning>
            {
                new()
                {
                    PartOfSpeech = "noun",
                    Definitions = new List<DTO.Definition> { new() { English = $"Definition of {word}", Chinese = $"{word}的定義" } },
                    ExampleSentences = new List<DTO.ExampleSentence>(),
                    Synonyms = new List<string>(),
                    Antonyms = new List<string>(),
                    RelatedWords = new List<string>()
                }
            }
        };

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
