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

namespace JackyAIApp.Server.Tests.Services
{
    public class ExamServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock;
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ExamService _service;
        private readonly string _testUserId = "test-user-123";

        public ExamServiceTests()
        {
            _currentUserMock = new Mock<ICurrentUser>();
            _promptServiceMock = new Mock<IOpenAIPromptService>();

            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);

            var generators = new List<IWordExamGenerator>
            {
                new ClozeExamGenerator(NullLogger<ClozeExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                new TranslationExamGenerator(NullLogger<TranslationExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
                new SentenceExamGenerator(NullLogger<SentenceExamGenerator>.Instance, _dbContext, _promptServiceMock.Object),
            };

            _service = new ExamService(
                NullLogger<ExamService>.Instance,
                _dbContext,
                _currentUserMock.Object,
                _promptServiceMock.Object,
                new SpacedRepetitionService(),
                generators,
                new ExamWordPicker(_dbContext),
                new UserAccountService(
                    NullLogger<UserAccountService>.Instance, _dbContext, _currentUserMock.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        public void Dispose() => _dbContext.Dispose();

        #region Cloze Tests

        [Fact]
        public async Task GenerateCloze_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GenerateWordExamAsync(ExamType.Cloze);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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

            var result = await _service.GenerateWordExamAsync(ExamType.Sentence);

            var dto = Assert.IsType<SentenceTestResponse>(result.Data);
            Assert.Equal(generatedTest.Prompt, dto.Prompt);
            var savedWord = await _dbContext.Words
                .Include(w => w.SentenceTests)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.SentenceTests);
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
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
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((expectedResponse, (string?)null));

            var result = await _service.RespondToConversationAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedResponse, result.Data);
        }

        [Fact]
        public async Task RespondToConversation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.RespondToConversationAsync(new ConversationResponseRequest
            {
                UserMessage = "Hello",
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = new List<ConversationTurn>(),
            });

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        private static ConversationStartRequest StartRequest() =>
            new() { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista" };

        [Fact]
        public async Task StartConversation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.StartConversationAsync(StartRequest());

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task StartConversation_WithoutARole_IsABadRequest()
        {
            await SeedUserAsync();

            var result = await _service.StartConversationAsync(
                new ConversationStartRequest { Scenario = "Coffee Shop", UserRole = "", AiRole = "Barista" });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
            _promptServiceMock.Verify(x => x.GetCompletionAsync<ConversationStartResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task StartConversation_ReturnsTheModelsOpening()
        {
            await SeedUserAsync();
            var opening = new ConversationStartResponse
            {
                Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista",
                Context = "A busy morning.", FirstMessage = "Hi! What can I get you?",
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ConversationStartResponse>(
                "Prompt/Exam/ConversationStartSystem.txt", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), "exam.conversation_start"))
                .ReturnsAsync((opening, (string?)null));

            var result = await _service.StartConversationAsync(StartRequest());

            Assert.Same(opening, result.Data);
        }

        [Fact]
        public async Task StartConversation_ModelFails_ReturnsError()
        {
            await SeedUserAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ConversationStartResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ConversationStartResponse?)null, "OpenAI error"));

            var result = await _service.StartConversationAsync(StartRequest());

            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task SummarizeConversation_WithoutHistory_IsABadRequest()
        {
            var result = await _service.SummarizeConversationAsync(new ConversationSummaryRequest
            {
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = new List<ConversationTurn>(),
            });

            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task SummarizeConversation_ReturnsTheModelsSummary()
        {
            var summary = new ConversationSummaryResponse
            {
                CorrectionsReview = ["I want → I'd like"], BetterPhrasings = [], SuggestedWords = ["latte"],
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ConversationSummaryResponse>(
                "Prompt/Exam/ConversationSummarySystem.txt", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((summary, (string?)null));

            var result = await _service.SummarizeConversationAsync(new ConversationSummaryRequest
            {
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = [new ConversationTurn { Speaker = "user", Message = "I want a latte." }],
            });

            Assert.Same(summary, result.Data);
        }

        [Fact]
        public async Task SummarizeConversation_ModelFails_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ConversationSummaryResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ConversationSummaryResponse?)null, "OpenAI error"));

            var result = await _service.SummarizeConversationAsync(new ConversationSummaryRequest
            {
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = [new ConversationTurn { Speaker = "user", Message = "I want a latte." }],
            });

            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        #endregion

        #region Transcribe / Evaluate Tests

        [Fact]
        public async Task TranscribeAudio_UserRowMissing_AsksToSignInAgain()
        {
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            _promptServiceMock.Verify(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task TranscribeAudio_ValidStream_ReturnsTranscription()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var transcriptionResult = new AudioTranscriptionResult("Hello world", 3.0, []);
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((transcriptionResult, (string?)null));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WhisperTranscriptionResponse>(result.Data);
            Assert.Equal("Hello world", dto.Text);
        }

        [Fact]
        public async Task TranscribeAudio_LongEnoughClip_IncludesDeliveryFeedback()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            // "This is a slow test sentence" over 10s -> well below the slow-pace threshold.
            var transcriptionResult = new AudioTranscriptionResult("This is a slow test sentence", 10.0, []);
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((transcriptionResult, (string?)null));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            var dto = Assert.IsType<WhisperTranscriptionResponse>(result.Data);
            Assert.NotNull(dto.DeliveryFeedback);
        }

        [Fact]
        public async Task TranscribeAudio_TooShortClip_OmitsDeliveryFeedback()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var transcriptionResult = new AudioTranscriptionResult("hi", 0.5, []);
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((transcriptionResult, (string?)null));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            var dto = Assert.IsType<WhisperTranscriptionResponse>(result.Data);
            Assert.Null(dto.DeliveryFeedback);
        }

        [Fact]
        public async Task TranscribeAudio_ServiceFails_ReturnsError()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(((AudioTranscriptionResult?)null, "Whisper transcription failed"));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
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

        #endregion

        #region Record Result Tests (listening, listening response, reading)

        [Fact]
        public async Task RecordExamResult_ListeningType_MissingListeningTestId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Listening",
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_WordBasedType_MissingWordId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Cloze",
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ListeningType_RecordsAttemptWithoutTouchingSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Listening",
                ListeningTestId = "lst-1",
                Correct = false,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);
            Assert.Null(dto.NextReviewDate);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Listening", attempt.ExamType);
            Assert.Equal("lst-1", attempt.ListeningTestId);
            Assert.Null(attempt.WordId);
            Assert.False(attempt.IsCorrect);
        }

        [Fact]
        public async Task RecordExamResult_ListeningResponseType_MissingId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "ListeningResponse", Correct = true };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ListeningResponseType_RecordsAttemptWithoutSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "ListeningResponse",
                ListeningResponseTestId = "lrt-1",
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("ListeningResponse", attempt.ExamType);
            Assert.Equal("lrt-1", attempt.ListeningResponseTestId);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task RecordExamResult_ReadingType_MissingId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Reading", Correct = true };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_ReadingType_RecordsAttemptWithoutSM2()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Reading",
                ReadingPassageId = "rp-1",
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Reading", attempt.ExamType);
            Assert.Equal("rp-1", attempt.ReadingPassageId);
            Assert.Null(attempt.WordId);
        }

        #endregion

        #region Writing Tests

        [Fact]
        public async Task GenerateWritingPrompt_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GenerateWritingPromptAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
        }

        [Fact]
        public async Task GenerateWritingPrompt_EmptyRepository_ReturnsPromptWithNoRequiredWords()
        {
            await SeedUserAsync();

            var result = await _service.GenerateWritingPromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.NotNull(dto.Topic);
            Assert.Empty(dto.RequiredWords);
        }

        [Fact]
        public async Task GenerateWritingPrompt_FewerThanFiveWords_ReturnsAllOfThem()
        {
            var words = await SeedUserWithWordsAsync(3);

            var result = await _service.GenerateWritingPromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.Equal(3, dto.RequiredWords.Count);
            Assert.Equal(
                words.Select(w => w.Id).OrderBy(x => x),
                dto.RequiredWords.Select(w => w.WordId).OrderBy(x => x));
        }

        [Fact]
        public async Task GenerateWritingPrompt_MoreThanFiveWords_ReturnsAtMostFiveDistinct()
        {
            await SeedUserWithWordsAsync(8);

            var result = await _service.GenerateWritingPromptAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingPromptResponse>(result.Data);
            Assert.Equal(5, dto.RequiredWords.Count);
            Assert.Equal(5, dto.RequiredWords.Select(w => w.WordId).Distinct().Count());
        }

        [Fact]
        public async Task GradeWriting_NullInput_ReturnsError()
        {
            var result = await _service.GradeWritingAsync(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeWriting_ReflectsUnusedRequiredWord()
        {
            await SeedUserAsync();
            var expected = new WritingGradingResponse
            {
                Score = 60,
                GrammarFeedback = "尚可。",
                ContentFeedback = "切題。",
                VocabularyFeedback = "有一個單字沒有使用到。",
                WordsUsed = new List<WritingWordUsageDto>
                {
                    new() { Word = "adventure", Used = true },
                    new() { Word = "journey", Used = false },
                },
                Suggestions = new List<string> { "試著用上 journey 這個字。" },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WritingGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((expected, (string?)null));

            var request = new WritingSubmissionRequest
            {
                Topic = "a memorable experience you had",
                RequiredWords = new List<string> { "adventure", "journey" },
                Submission = "My adventure last year was amazing.",
            };

            var result = await _service.GradeWritingAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<WritingGradingResponse>(result.Data);
            Assert.Equal(2, dto.WordsUsed.Count);
            Assert.True(dto.WordsUsed.Single(w => w.Word == "adventure").Used);
            Assert.False(dto.WordsUsed.Single(w => w.Word == "journey").Used);
        }

        [Fact]
        public async Task GradeWriting_PromptServiceFails_ReturnsError()
        {
            await SeedUserAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<WritingGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((WritingGradingResponse?)null, "OpenAI error"));

            var request = new WritingSubmissionRequest
            {
                Topic = "topic",
                RequiredWords = new List<string>(),
                Submission = "text",
            };

            var result = await _service.GradeWritingAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_MissingWritingWordIds_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Writing", Correct = true };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_EmptyWritingWordIds_AcceptedAndUpdatesNothing()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Writing",
                WritingWordIds = new List<string>(),
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Writing", attempt.ExamType);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task RecordExamResult_WritingType_AppliesSM2ToEveryMatchingRequiredWord()
        {
            var words = await SeedUserWithWordsAsync(3);
            // Third id has no matching UserWord (e.g. removed from the repository since the
            // prompt was fetched) — must be skipped, not error, and not counted toward the update.
            var missingId = Guid.NewGuid().ToString();
            var request = new RecordExamResultRequest
            {
                ExamType = "Writing",
                WritingWordIds = new List<string> { words[0].Id, words[1].Id, missingId },
                Correct = true,
                Score = 90,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.True(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Writing", attempt.ExamType);
            Assert.Null(attempt.WordId);

            var updatedWord0 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[0].Id);
            var updatedWord1 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[1].Id);
            var untouchedWord2 = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == words[2].Id);
            Assert.NotNull(updatedWord0.NextReviewDate);
            Assert.NotNull(updatedWord1.NextReviewDate);
            Assert.Null(untouchedWord2.NextReviewDate);
        }

        #endregion

        #region Dictation Tests

        [Fact]
        public async Task GenerateDictation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.GenerateDictationExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
        }

        [Fact]
        public async Task GenerateDictation_NoWordsAnywhere_ReturnsError()
        {
            await SeedUserAsync();

            var result = await _service.GenerateDictationExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
        }

        [Fact]
        public async Task GenerateDictation_ReturnsOnlyWordId_NoWordTextAnywhereInResponse()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GenerateDictationExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationPromptResponse>(result.Data);
            Assert.Equal(word.Id, dto.WordId);

            // Guards against a future DTO change accidentally leaking the answer: serialize the
            // response the way the API would and confirm the word text never appears in it.
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            Assert.DoesNotContain(word.WordText, json);
            Assert.Equal(
                new[] { "WordId" },
                dto.GetType().GetProperties().Select(p => p.Name).ToArray());
        }

        [Fact]
        public async Task GetDictationWordText_NotFound_ReturnsError()
        {
            var result = await _service.GetDictationWordTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetDictationWordText_Found_ReturnsWordText()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GetDictationWordTextAsync(word.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(word.WordText, result.Data);
        }

        [Fact]
        public async Task GradeDictation_MissingWordId_ReturnsError()
        {
            var result = await _service.GradeDictationAsync(new DictationGradeRequest { WordId = "", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeDictation_UnknownWordId_ReturnsNotFound()
        {
            var result = await _service.GradeDictationAsync(new DictationGradeRequest { WordId = "missing-id", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GradeDictation_ExactMatch_ReturnsCorrectWithFullScore()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeDictationAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "sun" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
            Assert.Equal("sun", dto.CorrectText);
        }

        [Fact]
        public async Task GradeDictation_CaseDifferenceOnly_StillCorrect()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeDictationAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "  SUN  " });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
        }

        [Fact]
        public async Task GradeDictation_GenuinelyWrong_ReturnsIncorrectWithPartialScore()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeDictationAsync(new DictationGradeRequest { WordId = word.Id, UserInput = "son" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.False(dto.IsCorrect);
            Assert.True(dto.Score > 0 && dto.Score < 100);
            Assert.Equal("sun", dto.CorrectText);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_MissingWordId_ReturnsError()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Dictation", Correct = true };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_UpdatesSM2LikeClozeWould()
        {
            var word = await SeedUserWithWordAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Dictation",
                WordId = word.Id,
                Correct = true,
                Score = 100,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.True(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Dictation", attempt.ExamType);
            Assert.Equal(word.Id, attempt.WordId);

            var updatedWord = await _dbContext.UserWords.SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == word.Id);
            Assert.NotNull(updatedWord.NextReviewDate);
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
                LastUpdated = DateTime.UtcNow
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return user;
        }

        private async Task<List<SQLWord>> SeedUserWithWordsAsync(int count)
        {
            await SeedUserAsync();

            var words = new List<SQLWord>();
            for (var i = 0; i < count; i++)
            {
                var word = new SQLWord
                {
                    Id = Guid.NewGuid().ToString(),
                    WordText = $"word{i}",
                    KKPhonics = "/wɜːrd/",
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
                words.Add(word);
            }
            await _dbContext.SaveChangesAsync();
            return words;
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
