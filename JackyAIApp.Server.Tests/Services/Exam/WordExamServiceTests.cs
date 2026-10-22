using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Exam.Generators;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>The vocabulary-based exams and their AI grading (<see cref="WordExamService"/>).</summary>
    public class WordExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly WordExamService _service;
        private readonly string _testUserId = "test-user-123";

        public WordExamServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new WordExamService(
                NullLogger<WordExamService>.Instance,
                _dbContext,
                _currentUserMock.Object,
                _promptServiceMock.Object,
                new List<IWordExamGenerator>
                {
                    new ClozeExamGenerator(NullLogger<ClozeExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                    new TranslationExamGenerator(NullLogger<TranslationExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                    new SentenceExamGenerator(NullLogger<SentenceExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                },
                new ExamWordPicker(_dbContext),
                new UserAccountService(
                NullLogger<UserAccountService>.Instance, _dbContext, _currentUserMock.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GenerateCloze_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateCloze_NoWordsAnywhere_ReturnsError()
        {
            await SeedUserAsync();

            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
            Assert.Contains("No words are available", result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateCloze_EmptyRepositoryButWordsExistGlobally_FallsBackToGlobalWord()
        {
            await SeedUserAsync();

            // A word exists in the dictionary but isn't in this user's repository.
            var word = new SQLWord
            {
                Id = Guid.NewGuid().ToString(),
                WordText = "moon",
                KKPhonics = "/muːn/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Words.Add(word);
            await _dbContext.SaveChangesAsync();

            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((new DTO.ClozeTest
                {
                    Question = "The __________ was full tonight.",
                    Answer = "moon",
                    Options = new List<string> { "moon", "moan", "mood", "moot" }
                }, (string?)null));

            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.True(result.IsSuccess);
            Assert.Equal(word.Id, ((DTO.ClozeTestResponse)result.Data!).WordId);
        }

        [Fact]
        public async Task GenerateCloze_ExistingTestAvailable_ReturnsExistingTest()
        {
            await SeedUserWithWordAndClozeTestsAsync(4);

            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DTO.ClozeTestResponse>(result.Data);
            Assert.NotNull(dto.Question);
            Assert.NotNull(dto.WordId);
            _promptServiceMock.Verify(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()), Times.Never);
        }

        [Fact]
        public async Task GenerateCloze_GeneratesNewTest_SavesToDB()
        {
            var word = await SeedUserWithWordAsync();
            var generatedTest = new DTO.ClozeTest
            {
                Question = "The ____ is shining brightly.",
                Answer = "sun",
                Options = new List<string> { "sun", "son", "sum", "sin" }
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generatedTest, (string?)null));

            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DTO.ClozeTestResponse>(result.Data);
            Assert.Equal(generatedTest.Question, dto.Question);
            Assert.Equal(word.Id, dto.WordId);

            var savedWord = await _dbContext.Words
                .Include(w => w.ClozeTests).ThenInclude(ct => ct.Options)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.ClozeTests);
            Assert.Equal(generatedTest.Question, savedWord.ClozeTests.First().Question);
        }

        [Fact]
        public async Task GenerateCloze_PromptServiceFails_ReturnsError()
        {
            await SeedUserWithWordAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((DTO.ClozeTest?)null, "OpenAI error"));

            var result = await _service.GenerateAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task GenerateTranslation_GeneratesNewTest_SavesToDB()
        {
            var word = await SeedUserWithWordAsync();
            var generatedTest = new DTO.TranslationTest
            {
                Chinese = "太陽很亮。",
                English = "The sun is very bright."
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.TranslationTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generatedTest, (string?)null));

            var result = await _service.GenerateAsync(ExamType.Translation);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<TranslationTestResponse>(result.Data);
            Assert.Equal(generatedTest.Chinese, dto.Chinese);

            var savedWord = await _dbContext.Words
                .Include(w => w.TranslationTests)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.TranslationTests);
        }

        [Fact]
        public async Task GenerateSentence_GeneratesNewTest_SavesToDB()
        {
            var word = await SeedUserWithWordAsync();
            var generatedTest = new DTO.SentenceTest
            {
                Prompt = "Use 'sun' to describe the weather.",
                SampleAnswer = "The sun came out after the rain.",
                Context = "Weather",
                DifficultyLevel = 2,
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.SentenceTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generatedTest, (string?)null));

            var result = await _service.GenerateAsync(ExamType.Sentence);

            var dto = Assert.IsType<SentenceTestResponse>(result.Data);
            Assert.Equal(generatedTest.Prompt, dto.Prompt);
            var savedWord = await _dbContext.Words
                .Include(w => w.SentenceTests)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.SentenceTests);
        }

        [Fact]
        public async Task GradeTranslation_NullInput_ReturnsError()
        {
            var result = await _service.GradeTranslationAsync(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeTranslation_ValidInput_ReturnsGrading()
        {
            var userResponse = new TranslationTestUserResponse
            {
                UnfamiliarWords = "sun",
                ExaminationQuestion = "太陽很亮。",
                Translation = "The sun is bright."
            };
            var expectedGrading = new TranslationQualityGradingAssistantResponse
            {
                Grade = "A",
                Reason = "翻譯準確",
                CorrectedTranslation = "The sun is bright.",
                WordUsed = true
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((expectedGrading, (string?)null));

            var result = await _service.GradeTranslationAsync(userResponse);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedGrading, result.Data);
        }

        [Fact]
        public async Task EvaluateSentence_ValidInput_ReturnsEvaluation()
        {
            var userResponse = new SentenceTestUserResponse
            {
                Word = "adventure",
                Prompt = "Use adventure in a sentence",
                Context = "Travel",
                UserSentence = "My adventure was amazing.",
                DifficultyLevel = 3
            };
            var expectedEvaluation = new SentenceTestGradingResponse
            {
                Score = 85,
                GrammarFeedback = "Good grammar",
                UsageFeedback = "Correct usage",
                CreativityFeedback = "Creative",
                OverallFeedback = "Great job!",
                Suggestions = new List<string> { "Add more details" }
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<SentenceTestGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((expectedEvaluation, (string?)null));

            var result = await _service.EvaluateSentenceAsync(userResponse);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedEvaluation, result.Data);
        }

        [Fact]
        public async Task EvaluateSentence_MissingInput_ReturnsError()
        {
            var result = await _service.EvaluateSentenceAsync(new SentenceTestUserResponse
            {
                Word = "adventure", Prompt = "Use adventure in a sentence", Context = "Travel", UserSentence = "", DifficultyLevel = 3,
            });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task EvaluateSentence_ModelFails_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<SentenceTestGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((SentenceTestGradingResponse?)null, "OpenAI error"));

            var result = await _service.EvaluateSentenceAsync(new SentenceTestUserResponse
            {
                Word = "adventure", Prompt = "Use adventure in a sentence", Context = "Travel", UserSentence = "My adventure was fun.", DifficultyLevel = 3,
            });

            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        private async Task<User> SeedUserAsync()
        {
            var user = new User
            {
                Id = _testUserId,
                Name = "Test User",
                Email = "test@example.com",
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return user;
        }

        private async Task<SQLWord> SeedUserWithWordAsync()
        {
            await SeedUserAsync();

            var word = new SQLWord
            {
                Id = Guid.NewGuid().ToString(),
                WordText = "sun",
                KKPhonics = "/sʌn/",
                DateAdded = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Words.Add(word);
            _dbContext.UserWords.Add(new UserWord
            {
                UserId = _testUserId,
                WordId = word.Id,
                DateAdded = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
            return word;
        }

        private async Task<SQLWord> SeedUserWithWordAndClozeTestsAsync(int testCount)
        {
            var word = await SeedUserWithWordAsync();

            for (int i = 0; i < testCount; i++)
            {
                var clozeTest = new Data.Models.SQL.ClozeTest
                {
                    Id = Guid.NewGuid().ToString(),
                    Question = $"Test question {i}",
                    Answer = "sun",
                    WordId = word.Id
                };
                clozeTest.Options.Add(new ClozeTestOption
                {
                    Id = Guid.NewGuid().ToString(),
                    OptionText = "sun",
                    ClozeTestId = clozeTest.Id
                });
                _dbContext.Add(clozeTest);
            }

            await _dbContext.SaveChangesAsync();
            return word;
        }
    }
}
