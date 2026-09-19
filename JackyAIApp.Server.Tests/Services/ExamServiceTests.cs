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
using Newtonsoft.Json;
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

        #endregion

        #region Transcribe / Evaluate Tests

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
        public async Task TranscribeAudio_ServiceFails_DoesNotConsumeExtraCredits()
        {
            await SeedUserAsync();
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(((AudioTranscriptionResult?)null, "Whisper transcription failed"));

            var result = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.False(result.IsSuccess);
            // Only the one AudioTranscription charge already asserted in setup — no second
            // ConsumeCreditsAsync call is made for delivery feedback (TASK-004: zero added cost).
            _creditServiceMock.Verify(x => x.ConsumeCreditsAsync(It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
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

        #endregion

        #region Listening Tests

        [Fact]
        public async Task GenerateListeningExam_EmptyBank_ReturnsError()
        {
            var result = await _service.GenerateListeningExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GenerateListeningExam_ReturnsQuestionWithOptions()
        {
            var listeningTest = await SeedListeningTestAsync();

            var result = await _service.GenerateListeningExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningTestResponse>(result.Data);
            Assert.Equal(listeningTest.Id, dto.Id);
            Assert.Equal(listeningTest.ImageUrl, dto.ImageUrl);
            Assert.Equal(4, dto.Options.Count);
            Assert.Single(dto.Options, o => o.IsCorrect);
        }

        [Fact]
        public async Task GetListeningOptionText_NotFound_ReturnsError()
        {
            var result = await _service.GetListeningOptionTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetListeningOptionText_Found_ReturnsText()
        {
            var listeningTest = await SeedListeningTestAsync();
            var option = listeningTest.Options.First();

            var result = await _service.GetListeningOptionTextAsync(option.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(option.Text, result.Data);
        }

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
            var listeningTest = await SeedListeningTestAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Listening",
                ListeningTestId = listeningTest.Id,
                Correct = false,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);
            Assert.Null(dto.NextReviewDate);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Listening", attempt.ExamType);
            Assert.Equal(listeningTest.Id, attempt.ListeningTestId);
            Assert.Null(attempt.WordId);
            Assert.False(attempt.IsCorrect);
        }

        [Fact]
        public async Task GenerateListeningExam_ExcludesRecentlyAttemptedQuestion()
        {
            var seen = await SeedListeningTestAsync();
            var unseen = await SeedListeningTestAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ListeningTestId = seen.Id,
                ExamType = "Listening",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();

            var result = await _service.GenerateListeningExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningTestResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        private async Task<ListeningTest> SeedListeningTestAsync()
        {
            var listeningTestId = Guid.NewGuid().ToString();
            var listeningTest = new ListeningTest
            {
                Id = listeningTestId,
                ImageUrl = "https://example.blob.core.windows.net/listening-images/lst-0001.jpg",
                ImagePrompt = "A man pointing at a chart on a whiteboard.",
                CreatedAt = DateTime.UtcNow,
                Options = new List<ListeningTestOption>
                {
                    new() { Id = Guid.NewGuid().ToString(), Text = "A man is pointing at a chart.", IsCorrect = true, Explanation = "正確。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "The people are seated.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "A woman is writing notes.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "The whiteboard is being removed.", IsCorrect = false, Explanation = "錯誤。", ListeningTestId = listeningTestId },
                },
            };
            _dbContext.ListeningTests.Add(listeningTest);
            await _dbContext.SaveChangesAsync();
            return listeningTest;
        }

        #endregion

        #region Listening Response Tests

        [Fact]
        public async Task GenerateListeningResponse_BankBelowThreshold_GeneratesAndPersists()
        {
            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "When is the quarterly report due?",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。" },
                    new() { Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。" },
                    new() { Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(generated.Prompt, dto.Prompt);
            Assert.Equal(3, dto.Options.Count);
            Assert.Single(dto.Options, o => o.IsCorrect);

            var saved = await _dbContext.ListeningResponseTests.Include(t => t.Options).SingleAsync();
            Assert.Equal(generated.Prompt, saved.Prompt);
            Assert.Equal(3, saved.Options.Count);
        }

        [Fact]
        public async Task GenerateListeningResponse_MalformedOptions_ReturnsError()
        {
            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "Bad shape",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "Only one option", IsCorrect = true, Explanation = "x" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
            Assert.Empty(await _dbContext.ListeningResponseTests.ToListAsync());
        }

        [Fact]
        public async Task GenerateListeningResponse_PromptServiceFails_EmptyBank_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task GenerateListeningResponse_PromptServiceFails_FallsBackToExistingBank()
        {
            var existing = await SeedListeningResponseTestAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(existing.Id, dto.Id);
        }

        [Fact]
        public async Task GetListeningResponsePromptText_NotFound_ReturnsError()
        {
            var result = await _service.GetListeningResponsePromptTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetListeningResponsePromptText_Found_ReturnsText()
        {
            var test = await SeedListeningResponseTestAsync();

            var result = await _service.GetListeningResponsePromptTextAsync(test.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(test.Prompt, result.Data);
        }

        [Fact]
        public async Task GetListeningResponseOptionText_Found_ReturnsText()
        {
            var test = await SeedListeningResponseTestAsync();
            var option = test.Options.First();

            var result = await _service.GetListeningResponseOptionTextAsync(option.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(option.Text, result.Data);
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
            var test = await SeedListeningResponseTestAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "ListeningResponse",
                ListeningResponseTestId = test.Id,
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("ListeningResponse", attempt.ExamType);
            Assert.Equal(test.Id, attempt.ListeningResponseTestId);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task GenerateListeningResponse_ExcludesRecentlyAttemptedQuestion()
        {
            var seen = await SeedListeningResponseTestAsync();
            var unseen = await SeedListeningResponseTestAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ListeningResponseTestId = seen.Id,
                ExamType = "ListeningResponse",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();
            // Bank stays below the growth threshold, so generation is always attempted first;
            // failing it forces the fallback-to-existing-bank path deterministically.
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ListeningResponseGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        [Fact]
        public async Task GenerateListeningResponse_LargeBankButSmallUnseenPool_ForcesGeneration()
        {
            // 22 total questions is well past the growth threshold (20), but the user has just
            // seen 10 of them — only 12 unseen, which is still below threshold and must force a
            // fresh AI generation rather than reusing from the (mostly-seen) bank.
            var seeded = new List<ListeningResponseTest>();
            for (var i = 0; i < 22; i++)
            {
                seeded.Add(await SeedListeningResponseTestAsync());
            }
            await SeedUserAsync();
            foreach (var test in seeded.Take(10))
            {
                _dbContext.ExamAttempts.Add(new ExamAttempt
                {
                    UserId = _testUserId,
                    ListeningResponseTestId = test.Id,
                    ExamType = "ListeningResponse",
                    IsCorrect = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await _dbContext.SaveChangesAsync();

            var generated = new ListeningResponseGenerationResult
            {
                Prompt = "Freshly generated prompt",
                Options = new List<ListeningResponseOptionGen>
                {
                    new() { Text = "Correct reply.", IsCorrect = true, Explanation = "正確。" },
                    new() { Text = "Wrong reply A.", IsCorrect = false, Explanation = "錯誤。" },
                    new() { Text = "Wrong reply B.", IsCorrect = false, Explanation = "錯誤。" },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ListeningResponseGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateListeningResponseExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ListeningResponseTestResponse>(result.Data);
            Assert.Equal(generated.Prompt, dto.Prompt);
            Assert.Equal(23, await _dbContext.ListeningResponseTests.CountAsync());
        }

        private async Task<ListeningResponseTest> SeedListeningResponseTestAsync()
        {
            var testId = Guid.NewGuid().ToString();
            var test = new ListeningResponseTest
            {
                Id = testId,
                Prompt = "When is the quarterly report due?",
                CreatedAt = DateTime.UtcNow,
                Options = new List<ListeningResponseOption>
                {
                    new() { Id = Guid.NewGuid().ToString(), Text = "By the end of this Friday.", IsCorrect = true, Explanation = "正確。", ListeningResponseTestId = testId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "In the third-floor conference room.", IsCorrect = false, Explanation = "錯誤。", ListeningResponseTestId = testId },
                    new() { Id = Guid.NewGuid().ToString(), Text = "Yes, I reported it already.", IsCorrect = false, Explanation = "錯誤。", ListeningResponseTestId = testId },
                },
            };
            _dbContext.ListeningResponseTests.Add(test);
            await _dbContext.SaveChangesAsync();
            return test;
        }

        #endregion

        #region Reading Tests

        [Fact]
        public async Task GenerateReading_BankBelowThreshold_GeneratesAndPersists()
        {
            var generated = BuildReadingGenerationResult();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(generated.Title, dto.Title);
            Assert.Equal(generated.PassageText, dto.PassageText);
            Assert.Equal(3, dto.Questions.Count);
            Assert.All(dto.Questions, q => Assert.Single(q.Options, o => o.IsCorrect));

            var saved = await _dbContext.ReadingPassages
                .Include(p => p.Questions).ThenInclude(q => q.Options)
                .SingleAsync();
            Assert.Equal(generated.Title, saved.Title);
            Assert.Equal(3, saved.Questions.Count);
        }

        [Fact]
        public async Task GenerateReading_MalformedQuestions_ReturnsError()
        {
            var generated = new ReadingGenerationResult
            {
                Title = "Bad shape",
                PassageText = "Some passage text.",
                Questions = new List<ReadingQuestionGen>
                {
                    new()
                    {
                        QuestionText = "Only one option?",
                        Options = new List<ReadingQuestionOptionGen>
                        {
                            new() { Text = "Only option", IsCorrect = true, Explanation = "x" },
                        },
                    },
                },
            };
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
            Assert.Empty(await _dbContext.ReadingPassages.ToListAsync());
        }

        [Fact]
        public async Task GenerateReading_ChinesePassage_IsNeverPersisted()
        {
            // The reported bug. The bank is shared and mostly read back rather than regenerated,
            // so persisting one Chinese passage serves it to every user until it is deleted by
            // hand — the write is the part that turns a bad answer into a lasting one.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "上週末，Maria 決定離開城市的喧囂，和兩個朋友開車上山。";
            chinese.Questions[0].QuestionText = "這篇文章主要在講什麼？";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Empty(await _dbContext.ReadingPassages.ToListAsync());
        }

        [Fact]
        public async Task GenerateReading_ChinesePassage_FallsBackToTheBankRatherThanFailing()
        {
            // With something already in the bank the user still gets a usable passage; only the
            // rejected one is thrown away.
            var existing = await SeedReadingPassageAsync();
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(existing.Id, Assert.IsType<ReadingPassageResponse>(result.Data).Id);
            Assert.Single(await _dbContext.ReadingPassages.ToListAsync()); // nothing new saved
        }

        [Fact]
        public async Task GenerateReading_RetriesOnceWhenTheFirstAttemptIsInTheWrongLanguage()
        {
            // Answering in the wrong language is a sampling slip, not a refusal, so one more roll
            // is usually enough and is cheaper than giving up on generating at all.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            var good = BuildReadingGenerationResult();
            _promptServiceMock.SetupSequence(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null))
                .ReturnsAsync((good, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(good.PassageText, Assert.IsType<ReadingPassageResponse>(result.Data).PassageText);
            Assert.Equal(good.Title, (await _dbContext.ReadingPassages.SingleAsync()).Title);
        }

        [Fact]
        public async Task GenerateReading_DoesNotRetryForever()
        {
            // Two attempts and no more: a model that is going to comply does so on the retry, and
            // past that the credit spend buys nothing the fallback does not already give.
            var chinese = BuildReadingGenerationResult();
            chinese.PassageText = "整段都是中文的文章。";
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((chinese, (string?)null));

            await _service.GenerateReadingExamAsync();

            _promptServiceMock.Verify(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task GenerateReading_PromptServiceFails_EmptyBank_ReturnsError()
        {
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateReadingExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task GenerateReading_PromptServiceFails_FallsBackToExistingBank()
        {
            var existing = await SeedReadingPassageAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(existing.Id, dto.Id);
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
            var passage = await SeedReadingPassageAsync();
            var request = new RecordExamResultRequest
            {
                ExamType = "Reading",
                ReadingPassageId = passage.Id,
                Correct = true,
            };

            var result = await _service.RecordExamResultAsync(request);

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<RecordExamResultResponse>(result.Data);
            Assert.False(dto.ScheduleUpdated);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Reading", attempt.ExamType);
            Assert.Equal(passage.Id, attempt.ReadingPassageId);
            Assert.Null(attempt.WordId);
        }

        [Fact]
        public async Task GenerateReading_ExcludesRecentlyAttemptedPassage()
        {
            var seen = await SeedReadingPassageAsync();
            var unseen = await SeedReadingPassageAsync();
            await SeedUserAsync();
            _dbContext.ExamAttempts.Add(new ExamAttempt
            {
                UserId = _testUserId,
                ReadingPassageId = seen.Id,
                ExamType = "Reading",
                IsCorrect = true,
                CreatedAt = DateTime.UtcNow,
            });
            await _dbContext.SaveChangesAsync();
            // Bank stays below the growth threshold, so generation is always attempted first;
            // failing it forces the fallback-to-existing-bank path deterministically.
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync(((ReadingGenerationResult?)null, "OpenAI error"));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(unseen.Id, dto.Id);
        }

        [Fact]
        public async Task GenerateReading_LargeBankButSmallUnseenPool_ForcesGeneration()
        {
            // 22 total passages is well past the growth threshold (20), but the user has just
            // seen 10 of them — only 12 unseen, which is still below threshold and must force a
            // fresh AI generation rather than reusing from the (mostly-seen) bank.
            var seeded = new List<ReadingPassage>();
            for (var i = 0; i < 22; i++)
            {
                seeded.Add(await SeedReadingPassageAsync());
            }
            await SeedUserAsync();
            foreach (var passage in seeded.Take(10))
            {
                _dbContext.ExamAttempts.Add(new ExamAttempt
                {
                    UserId = _testUserId,
                    ReadingPassageId = passage.Id,
                    ExamType = "Reading",
                    IsCorrect = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await _dbContext.SaveChangesAsync();

            var generated = BuildReadingGenerationResult();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<ReadingGenerationResult>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .ReturnsAsync((generated, (string?)null));

            var result = await _service.GenerateReadingExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<ReadingPassageResponse>(result.Data);
            Assert.Equal(generated.Title, dto.Title);
            Assert.Equal(23, await _dbContext.ReadingPassages.CountAsync());
        }

        private static ReadingGenerationResult BuildReadingGenerationResult() => new()
        {
            Title = "A Weekend Trip",
            PassageText = "Maria drove to the mountains with two friends for a short weekend hike.",
            Questions = new List<ReadingQuestionGen>
            {
                new()
                {
                    QuestionText = "What is the passage mainly about?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "A hiking trip.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "A cooking class.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "A job interview.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
                new()
                {
                    QuestionText = "Who did Maria travel with?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "Two friends.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "Her parents.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "Her coworkers.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
                new()
                {
                    QuestionText = "How long was the trip?",
                    Options = new List<ReadingQuestionOptionGen>
                    {
                        new() { Text = "A weekend.", IsCorrect = true, Explanation = "正確。" },
                        new() { Text = "A whole month.", IsCorrect = false, Explanation = "錯誤。" },
                        new() { Text = "A single afternoon.", IsCorrect = false, Explanation = "錯誤。" },
                    },
                },
            },
        };

        private async Task<ReadingPassage> SeedReadingPassageAsync()
        {
            var passageId = Guid.NewGuid().ToString();
            var questionId = Guid.NewGuid().ToString();
            var passage = new ReadingPassage
            {
                Id = passageId,
                Title = "A Weekend Trip",
                PassageText = "Maria drove to the mountains with two friends for a short weekend hike.",
                CreatedAt = DateTime.UtcNow,
                Questions = new List<ReadingQuestion>
                {
                    new()
                    {
                        Id = questionId,
                        QuestionText = "What is the passage mainly about?",
                        PassageId = passageId,
                        Options = new List<ReadingQuestionOption>
                        {
                            new() { Id = Guid.NewGuid().ToString(), Text = "A hiking trip.", IsCorrect = true, Explanation = "正確。", QuestionId = questionId },
                            new() { Id = Guid.NewGuid().ToString(), Text = "A cooking class.", IsCorrect = false, Explanation = "錯誤。", QuestionId = questionId },
                            new() { Id = Guid.NewGuid().ToString(), Text = "A job interview.", IsCorrect = false, Explanation = "錯誤。", QuestionId = questionId },
                        },
                    },
                },
            };
            _dbContext.ReadingPassages.Add(passage);
            await _dbContext.SaveChangesAsync();
            return passage;
        }

        #endregion

        #region Writing Tests

        [Fact]
        public async Task GenerateWritingPrompt_UserNotFound_ReturnsError()
        {
            var result = await _service.GenerateWritingPromptAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
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
        public async Task GenerateWritingPrompt_NeverConsumesCredits()
        {
            // TASK-008: the one exam-type "generation" endpoint with zero underlying AI cost —
            // must never be credit-gated, unlike every other GET api/exam/* endpoint.
            await SeedUserWithWordsAsync(5);

            var result = await _service.GenerateWritingPromptAsync();

            Assert.True(result.IsSuccess);
            _creditServiceMock.Verify(x => x.ConsumeCreditsAsync(
                It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GradeWriting_NullInput_ReturnsError()
        {
            var result = await _service.GradeWritingAsync(null!);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeWriting_InsufficientCredits_ReturnsForbidden()
        {
            await SeedUserAsync();
            _creditServiceMock.Setup(x => x.ConsumeCreditsAsync(_testUserId, CreditCosts.WritingEvaluation, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            var request = new WritingSubmissionRequest
            {
                Topic = "a memorable experience you had",
                RequiredWords = new List<string> { "adventure" },
                Submission = "Some paragraph.",
            };

            var result = await _service.GradeWritingAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
            _promptServiceMock.Verify(x => x.GetCompletionAsync<WritingGradingResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GradeWriting_ChargesCreditsExactlyOnce_AndReflectsUnusedRequiredWord()
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
            _creditServiceMock.Verify(x => x.ConsumeCreditsAsync(_testUserId, CreditCosts.WritingEvaluation, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
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
        public async Task GenerateDictationExam_UserNotFound_ReturnsError()
        {
            var result = await _service.GenerateDictationExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
        }

        [Fact]
        public async Task GenerateDictationExam_NoWordsAnywhere_ReturnsError()
        {
            await SeedUserAsync();

            var result = await _service.GenerateDictationExamAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.Forbidden, result.Error);
            Assert.Contains("No words are available", result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateDictationExam_ReturnsWordId_AndResponseJsonLeaksNoWordText()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GenerateDictationExamAsync();

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationPromptResponse>(result.Data);
            Assert.Equal(word.Id, dto.WordId);

            // Guards against a future DTO change accidentally leaking the answer: dictation's
            // whole point is guessing the word from audio alone, unlike Cloze/Translation.
            var json = JsonConvert.SerializeObject(dto);
            Assert.DoesNotContain(word.WordText, json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetDictationWordText_NotFound_ReturnsError()
        {
            var result = await _service.GetDictationWordTextAsync("missing-id");

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GetDictationWordText_Found_ReturnsSameTextGradeWouldScoreAgainst()
        {
            var word = await SeedUserWithWordAsync();

            var audioLookup = await _service.GetDictationWordTextAsync(word.Id);
            var gradeResult = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = word.Id, UserInput = word.WordText });

            Assert.True(audioLookup.IsSuccess);
            Assert.Equal(word.WordText, audioLookup.Data);
            var gradeDto = Assert.IsType<DictationGradeResponse>(gradeResult.Data);
            Assert.Equal(audioLookup.Data, gradeDto.CorrectText);
        }

        [Fact]
        public async Task GradeDictation_MissingWordId_ReturnsError()
        {
            var result = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = "", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task GradeDictation_WordNotFound_ReturnsError()
        {
            var result = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = "missing-id", UserInput = "sun" });

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.NotFound, result.Error);
        }

        [Fact]
        public async Task GradeDictation_ExactMatch_ReturnsCorrectWithFullScore()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = word.Id, UserInput = "sun" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
            Assert.Equal("sun", dto.CorrectText);
        }

        [Fact]
        public async Task GradeDictation_CaseAndWhitespaceOnlyDifference_StillCorrect()
        {
            var word = await SeedUserWithWordAsync();

            var result = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = word.Id, UserInput = "  SUN  " });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.True(dto.IsCorrect);
            Assert.Equal(100, dto.Score);
        }

        [Fact]
        public async Task GradeDictation_WrongButClose_ReturnsIncorrectWithPartialScore()
        {
            var word = await SeedUserWithWordAsync(); // "sun"

            var result = await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = word.Id, UserInput = "sin" });

            Assert.True(result.IsSuccess);
            var dto = Assert.IsType<DictationGradeResponse>(result.Data);
            Assert.False(dto.IsCorrect);
            Assert.InRange(dto.Score, 1, 99);
        }

        [Fact]
        public async Task GradeDictation_NeverConsumesCredits()
        {
            var word = await SeedUserWithWordAsync();

            await _service.GradeDictationAsync(
                new DictationGradeRequest { WordId = word.Id, UserInput = "sun" });
            await _service.GetDictationWordTextAsync(word.Id);

            _creditServiceMock.Verify(x => x.ConsumeCreditsAsync(
                It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_RequiresWordId_LikeAnyWordAnchoredType()
        {
            await SeedUserAsync();
            var request = new RecordExamResultRequest { ExamType = "Dictation", Correct = true };

            var result = await _service.RecordExamResultAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.BadRequest, result.Error);
        }

        [Fact]
        public async Task RecordExamResult_DictationType_UpdatesSM2ScheduleLikeClozeWould()
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
            Assert.NotNull(dto.NextReviewDate);

            var attempt = await _dbContext.ExamAttempts.SingleAsync(a => a.UserId == _testUserId);
            Assert.Equal("Dictation", attempt.ExamType);
            Assert.Equal(word.Id, attempt.WordId);
            Assert.True(attempt.IsCorrect);

            var updatedUserWord = await _dbContext.UserWords
                .SingleAsync(uw => uw.UserId == _testUserId && uw.WordId == word.Id);
            Assert.NotNull(updatedUserWord.NextReviewDate);
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
