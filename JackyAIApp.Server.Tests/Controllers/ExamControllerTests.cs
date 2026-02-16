using Betalgo.Ranul.OpenAI.Interfaces;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SQLWord = JackyAIApp.Server.Data.Models.SQL.Word;

namespace JackyAIApp.Server.Tests.Controllers
{
    public class ExamControllerTests : IDisposable
    {
        private readonly Mock<ILogger<ExamController>> _loggerMock;
        private readonly Mock<IMyResponseFactory> _responseFactoryMock;
        private readonly Mock<IUserService> _userServiceMock;
        private readonly Mock<IOpenAIService> _openAIServiceMock;
        private readonly Mock<IOpenAIPromptService> _promptServiceMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ExamController _controller;
        private readonly string _testUserId = "test-user-123";

        public ExamControllerTests()
        {
            _loggerMock = new Mock<ILogger<ExamController>>();
            _responseFactoryMock = new Mock<IMyResponseFactory>();
            _userServiceMock = new Mock<IUserService>();
            _openAIServiceMock = new Mock<IOpenAIService>();
            _promptServiceMock = new Mock<IOpenAIPromptService>();

            // Setup in-memory database
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            // Setup user service
            _userServiceMock.Setup(x => x.GetUserId()).Returns(_testUserId);

            // Setup response factory
            _responseFactoryMock.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(new { Success = true, Data = data }));
            _responseFactoryMock.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new BadRequestObjectResult(new { Success = false, Message = msg }));

            _controller = new ExamController(
                _loggerMock.Object,
                _responseFactoryMock.Object,
                _dbContext,
                _userServiceMock.Object,
                _openAIServiceMock.Object,
                _promptServiceMock.Object);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        #region GetClozeTest Tests

        [Fact]
        public async Task GetClozeTest_UserNotFound_ReturnsError()
        {
            // Arrange - no user in DB

            // Act
            var result = await _controller.GetClozeTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.Forbidden, 
                "User not found."), Times.Once);
        }

        [Fact]
        public async Task GetClozeTest_NoWords_ReturnsError()
        {
            // Arrange
            await SeedUserAsync();

            // Act
            var result = await _controller.GetClozeTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.Forbidden, 
                It.Is<string>(s => s.Contains("haven't added any unfamiliar words"))), Times.Once);
        }

        [Fact]
        public async Task GetClozeTest_ExistingTestAvailable_ReturnsExistingTest()
        {
            // Arrange
            var word = await SeedUserWithWordAndClozeTestsAsync(4);

            // Act
            var result = await _controller.GetClozeTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<DTO.ClozeTest>(ct => ct.Question != null)), Times.Once);
            // Should NOT call prompt service since we have enough tests
            _promptServiceMock.Verify(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<IEnumerable<(string, string)>>()), Times.Never);
        }

        [Fact]
        public async Task GetClozeTest_GeneratesNewTest_SavesToDB()
        {
            // Arrange
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

            // Act
            var result = await _controller.GetClozeTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<DTO.ClozeTest>(ct => ct.Question == generatedTest.Question)), Times.Once);

            // Verify saved to DB
            var savedWord = await _dbContext.Words
                .Include(w => w.ClozeTests)
                .ThenInclude(ct => ct.Options)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.ClozeTests);
            Assert.Equal(generatedTest.Question, savedWord.ClozeTests.First().Question);
        }

        [Fact]
        public async Task GetClozeTest_PromptServiceFails_ReturnsError()
        {
            // Arrange
            await SeedUserWithWordAsync();
            _promptServiceMock.Setup(x => x.GetCompletionAsync<DTO.ClozeTest>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync(((DTO.ClozeTest?)null, "OpenAI error"));

            // Act
            var result = await _controller.GetClozeTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.QueryOpenAIFailed, 
                It.IsAny<string>()), Times.Once);
        }

        #endregion

        #region GetTranslationTest Tests

        [Fact]
        public async Task GetTranslationTest_GeneratesNewTest_SavesToDB()
        {
            // Arrange
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

            // Act
            var result = await _controller.GetTranslationTest();

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<TranslationTestResponse>(tt => tt.Chinese == generatedTest.Chinese)), Times.Once);

            // Verify saved to DB
            var savedWord = await _dbContext.Words
                .Include(w => w.TranslationTests)
                .FirstAsync(w => w.Id == word.Id);
            Assert.Single(savedWord.TranslationTests);
        }

        #endregion

        #region GetTranslationQualityGrading Tests

        [Fact]
        public async Task GetTranslationQualityGrading_NullInput_ReturnsError()
        {
            // Act
            var result = await _controller.GetTranslationQualityGrading(null!);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.BadRequest, 
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task GetTranslationQualityGrading_ValidInput_ReturnsGrading()
        {
            // Arrange
            var userResponse = new TranslationTestUserResponse
            {
                UnfamiliarWords = "sun",
                ExaminationQuestion = "太陽很亮。",
                Translation = "The sun is bright."
            };

            var expectedGrading = new TranslationQualityGradingAssistantResponse
            {
                TranslationQualityGrading = "A級 原因：翻譯準確"
            };

            _promptServiceMock.Setup(x => x.GetCompletionAsync<TranslationQualityGradingAssistantResponse>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<(string, string)>>()))
                .ReturnsAsync((expectedGrading, (string?)null));

            // Act
            var result = await _controller.GetTranslationQualityGrading(userResponse);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(expectedGrading), Times.Once);
        }

        #endregion

        #region RespondToConversation Tests

        [Fact]
        public async Task RespondToConversation_InvalidRequest_ReturnsError()
        {
            // Act
            var result = await _controller.RespondToConversation(null!);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.BadRequest, 
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RespondToConversation_ValidRequest_ReturnsResponse()
        {
            // Arrange
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

            // Act
            var result = await _controller.RespondToConversation(request);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(expectedResponse), Times.Once);
        }

        #endregion

        #region TranscribeAudio Tests

        [Fact]
        public async Task TranscribeAudio_NoFile_ReturnsError()
        {
            // Act
            var result = await _controller.TranscribeAudio(null!);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.BadRequest, 
                "No audio file provided."), Times.Once);
        }

        [Fact]
        public async Task TranscribeAudio_ValidFile_ReturnsTranscription()
        {
            // Arrange
            await SeedUserAsync();
            var mockFile = CreateMockFormFile("test.mp3", new byte[] { 1, 2, 3 });

            _promptServiceMock.Setup(x => x.TranscribeAudioAsync(
                It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(("Hello world", (string?)null));

            // Act
            var result = await _controller.TranscribeAudio(mockFile);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(
                It.Is<WhisperTranscriptionResponse>(r => r.Text == "Hello world")), Times.Once);
        }

        #endregion

        #region EvaluateSentence Tests

        [Fact]
        public async Task EvaluateSentence_ValidInput_ReturnsEvaluation()
        {
            // Arrange
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

            // Act
            var result = await _controller.EvaluateSentence(userResponse);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateOKResponse(expectedEvaluation), Times.Once);
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

            var userWord = new UserWord
            {
                UserId = _testUserId,
                WordId = word.Id,
                DateAdded = DateTime.UtcNow
            };
            _dbContext.UserWords.Add(userWord);

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

        private static Microsoft.AspNetCore.Http.IFormFile CreateMockFormFile(string fileName, byte[] content)
        {
            var stream = new MemoryStream(content);
            var formFile = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
            formFile.Setup(f => f.FileName).Returns(fileName);
            formFile.Setup(f => f.Length).Returns(content.Length);
            formFile.Setup(f => f.OpenReadStream()).Returns(stream);
            return formFile.Object;
        }

        #endregion
    }
}
