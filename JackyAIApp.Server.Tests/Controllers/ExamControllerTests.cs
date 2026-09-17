using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Speech;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// Controller-level tests for behavior that lives in the controller itself
    /// (HTTP concerns). The domain logic is covered by <see cref="Services.ExamServiceTests"/>.
    /// </summary>
    public class ExamControllerTests : IDisposable
    {
        private readonly Mock<IMyResponseFactory> _responseFactoryMock;
        private readonly Mock<IExamService> _examServiceMock;
        private readonly Mock<ITextToSpeechService> _ttsServiceMock;
        private readonly Mock<IUserService> _userServiceMock;
        private readonly Mock<IListeningQuestionGenerator> _listeningQuestionGeneratorMock;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ExamController _controller;

        public ExamControllerTests()
        {
            _responseFactoryMock = new Mock<IMyResponseFactory>();
            _examServiceMock = new Mock<IExamService>();
            _ttsServiceMock = new Mock<ITextToSpeechService>();
            _userServiceMock = new Mock<IUserService>();
            _listeningQuestionGeneratorMock = new Mock<IListeningQuestionGenerator>();

            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _responseFactoryMock.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(new { Success = true, Data = data }));
            _responseFactoryMock.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new BadRequestObjectResult(new { Success = false, Message = msg }));

            _controller = new ExamController(
                _responseFactoryMock.Object, _examServiceMock.Object, _ttsServiceMock.Object,
                _userServiceMock.Object, _listeningQuestionGeneratorMock.Object, _dbContext);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetWritingPrompt_CallsServiceDirectly_NoConsumeCreditsAttribute()
        {
            // GetWritingPrompt has no [ConsumeCredits] attribute — verified structurally here by
            // simply confirming it forwards straight to the service; the "never credit-gated"
            // behavior itself is covered service-side (ExamServiceTests.GenerateWritingPrompt_NeverConsumesCredits).
            _examServiceMock.Setup(x => x.GenerateWritingPromptAsync())
                .ReturnsAsync(ServiceResult.Ok(new { Topic = "daily life", RequiredWords = new List<object>() }));

            var result = await _controller.GetWritingPrompt();

            Assert.IsType<OkObjectResult>(result);
            _examServiceMock.Verify(x => x.GenerateWritingPromptAsync(), Times.Once);
        }

        [Fact]
        public async Task TranscribeAudio_NoFile_ReturnsError()
        {
            // Act
            var result = await _controller.TranscribeAudio(null!);

            // Assert
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.BadRequest,
                "No audio file provided."), Times.Once);
            // Service should not be touched when there is no file.
            _examServiceMock.Verify(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GenerateListeningQuestion_NonAdmin_ReturnsForbidden()
        {
            await SeedUserAsync("user-1", isAdmin: false);
            _userServiceMock.Setup(x => x.GetUserId()).Returns("user-1");

            var result = await _controller.GenerateListeningQuestion();

            Assert.IsType<BadRequestObjectResult>(result);
            _responseFactoryMock.Verify(x => x.CreateErrorResponse(ErrorCodes.Forbidden, It.IsAny<string>()), Times.Once);
            _listeningQuestionGeneratorMock.Verify(x => x.GenerateAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GenerateListeningQuestion_Admin_CallsGenerator()
        {
            await SeedUserAsync("admin-1", isAdmin: true);
            _userServiceMock.Setup(x => x.GetUserId()).Returns("admin-1");
            _listeningQuestionGeneratorMock.Setup(x => x.GenerateAsync("admin-1"))
                .ReturnsAsync(ServiceResult.Ok(new { Id = "lst-new" }));

            var result = await _controller.GenerateListeningQuestion();

            Assert.IsType<OkObjectResult>(result);
            _listeningQuestionGeneratorMock.Verify(x => x.GenerateAsync("admin-1"), Times.Once);
        }

        [Fact]
        public async Task GetListeningBank_NonAdmin_ReturnsForbidden()
        {
            await SeedUserAsync("user-2", isAdmin: false);
            _userServiceMock.Setup(x => x.GetUserId()).Returns("user-2");

            var result = await _controller.GetListeningBank();

            Assert.IsType<BadRequestObjectResult>(result);
            _examServiceMock.Verify(x => x.GetListeningBankAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        private async Task<Data.Models.SQL.User> SeedUserAsync(string userId, bool isAdmin)
        {
            var user = new Data.Models.SQL.User
            {
                Id = userId,
                CreditBalance = 100,
                TotalCreditsUsed = 0,
                LastUpdated = DateTime.UtcNow,
                IsAdmin = isAdmin,
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
            return user;
        }
    }
}
