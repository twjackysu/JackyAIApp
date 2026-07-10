using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Exam.Generators;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Services
{
    public class ExamServiceTests : IDisposable
    {
        private readonly Mock<IUserService> _userServiceMock;
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly Mock<ICreditService> _creditServiceMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ExamService _service;
        private readonly string _testUserId = "test-user-123";

        public ExamServiceTests()
        {
            _userServiceMock = new Mock<IUserService>();
            _promptServiceMock = new Mock<IOpenAIPromptService>();
            _creditServiceMock = new Mock<ICreditService>();

            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _userServiceMock.Setup(x => x.GetUserId()).Returns(_testUserId);
            _creditServiceMock.Setup(x => x.ConsumeCreditsAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var generators = new List<IWordExamGenerator>
            {
                new ClozeExamGenerator(NullLogger<ClozeExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                new TranslationExamGenerator(NullLogger<TranslationExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                new SentenceExamGenerator(NullLogger<SentenceExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
            };

            _service = new ExamService(
                NullLogger<ExamService>.Instance,
                _dbContext,
                _userServiceMock.Object,
                _promptServiceMock.Object,
                _creditServiceMock.Object,
                new SpacedRepetitionService(),
                generators);
        }

        public void Dispose() => _dbContext.Dispose();

        #region Cloze Tests

        [Fact]
        public async Task GenerateCloze_UserNotFound_ReturnsError()
        {
            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
            Assert.Equal("User not found.", result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateCloze_NoWordsAnywhere_ReturnsError()
        {
            await SeedUserAsync();

            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((new DTO.ClozeTest
                {
                    Question = "The __________ was full tonight.",
                    Answer = "moon",
                    Options = new List<string> { "moon", "moan", "mood", "moot" }
                }, (string?)null));

            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

            Assert.True(result.IsSuccess);
            Assert.Equal(word.Id, ((DTO.ClozeTestResponse)result.Data!).WordId);
        }

        [Fact]
        public async Task GenerateCloze_ExistingTestAvailable_ReturnsExistingTest()
        {
            await SeedUserWithWordAndClozeTestsAsync(4);

            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((generatedTest, (string?)null));

            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync(((DTO.ClozeTest?)null, "OpenAI error"));

            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        #endregion

        #region Translation Tests

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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((generatedTest, (string?)null));

            var result = await _service.GenerateWordExamAsync(ExamType.Translation);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<TranslationTestResponse>(result.Data);
            Assert.Equal(generatedTest.Chinese, dto.Chinese);

            var savedWord = await _dbContext.Words
                .Include(w => w.TranslationTests)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.TranslationTests);
        }

        #endregion

        #region Translation Quality Grading Tests

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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((expectedGrading, (string?)null));

            var result = await _service.GradeTranslationAsync(userResponse);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedGrading, result.Data);
        }

        #endregion

        #region Conversation Tests

        [Fact]
        public async Task RespondToConversation_InvalidRequest_ReturnsError()
        {
            var result = await _service.RespondToConversationAsync(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RespondToConversation_ValidRequest_ReturnsResponse()
        {
            await SeedUserAsync();
            var request = new ConversationResponseRequest
            {
                UserMessage = "Hello",
                ConversationContext = new ConversationContext
                {
                    Scenario = "Coffee Shop",
                    UserRole = "Customer",
                    AiRole = "Barista",
                    TurnNumber = 1
                },
                ConversationHistory = new List<ConversationTurn>()
            };
            var expectedResponse = new ConversationResponseResponse
            {
                AiResponse = "Hello! Welcome to our coffee shop. What can I get for you today?",
                Correction = new ConversationCorrection { HasCorrection = false }
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ConversationResponseResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((expectedResponse, (string?)null));

            var result = await _service.RespondToConversationAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedResponse, result.Data);
        }

        #endregion

        #region Transcribe / Evaluate Tests

        [Fact]
        public async Task TranscribeAudio_ValidStream_ReturnsTranscription()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(("Hello world", (string?)null));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WhisperTranscriptionResponse>(result.Data);
            Assert.Equal("Hello world", dto.Text);
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
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((expectedEvaluation, (string?)null));

            var result = await _service.EvaluateSentenceAsync(userResponse);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedEvaluation, result.Data);
        }

        #endregion

        #region Helper Methods

        private async Task<User> SeedUserAsync()
        {
            var user = new User
            {
                Id = _testUserId,
                Name = "Test User",
                Email = "test@example.com",
                CreditBalance = 100,
                TotalCreditsUsed = 0,
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

        #endregion
    }
}
