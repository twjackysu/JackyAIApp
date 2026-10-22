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

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>Conversation practice and transcription (<see cref="ConversationPracticeService"/>).</summary>
    public class ConversationPracticeServiceTests : IDisposable
    {
        private readonly Mock<ICurrentUser> _currentUserMock = new();
        private readonly Mock<IOpenAIPromptService> _promptServiceMock = new();
        private readonly AzureSQLDBContext _dbContext = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);
        private readonly ConversationPracticeService _service;
        private readonly string _testUserId = "test-user-123";

        public ConversationPracticeServiceTests()
        {
            _currentUserMock.Setup(x => x.Id).Returns(_testUserId);
            _service = new ConversationPracticeService(
                NullLogger<ConversationPracticeService>.Instance,
                _promptServiceMock.Object,
                new UserAccountService(
                NullLogger<UserAccountService>.Instance, _dbContext, _currentUserMock.Object, Mock.Of<IDrawdownBrakeAccess>()));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task RespondToConversation_InvalidRequest_ReturnsError()
        {
            var result = await _service.RespondAsync(null!);

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

            var result = await _service.RespondAsync(request);

            Assert.True(result.IsSuccess);
            Assert.Same(expectedResponse, result.Data);
        }

        [Fact]
        public async Task RespondToConversation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.RespondAsync(new ConversationResponseRequest
            {
                UserMessage = "Hello",
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = new List<ConversationTurn>(),
            });

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task StartConversation_UserRowMissing_AsksToSignInAgain()
        {
            var result = await _service.StartAsync(StartRequest());

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task StartConversation_WithoutARole_IsABadRequest()
        {
            await SeedUserAsync();

            var result = await _service.StartAsync(
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

            var result = await _service.StartAsync(StartRequest());

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

            var result = await _service.StartAsync(StartRequest());

            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

        [Fact]
        public async Task SummarizeConversation_WithoutHistory_IsABadRequest()
        {
            var result = await _service.SummarizeAsync(new ConversationSummaryRequest
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

            var result = await _service.SummarizeAsync(new ConversationSummaryRequest
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

            var result = await _service.SummarizeAsync(new ConversationSummaryRequest
            {
                ConversationContext = new ConversationContext { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista", TurnNumber = 3 },
                ConversationHistory = [new ConversationTurn { Speaker = "user", Message = "I want a latte." }],
            });

            Assert.Equal(ErrorCodes.QueryOpenAIFailed, result.Error);
        }

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

        private static ConversationStartRequest StartRequest() =>
            new() { Scenario = "Coffee Shop", UserRole = "Customer", AiRole = "Barista" };

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
    }
}
