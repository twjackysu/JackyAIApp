using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Speech;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// Controller-level tests for behavior that lives in the controller itself
    /// (HTTP concerns). The domain logic is covered by <see cref="Services.ExamServiceTests"/>.
    /// </summary>
    public class ExamControllerTests
    {
        private readonly Mock<IMyResponseFactory> _responseFactoryMock;
        private readonly Mock<IExamService> _examServiceMock;
        private readonly Mock<ITextToSpeechService> _ttsServiceMock;
        private readonly Mock<ICurrentUser> _currentUserMock;
        private readonly Mock<IListeningQuestionGenerator> _listeningQuestionGeneratorMock;
        private readonly ExamController _controller;

        public ExamControllerTests()
        {
            _responseFactoryMock = new Mock<IMyResponseFactory>();
            _examServiceMock = new Mock<IExamService>();
            _ttsServiceMock = new Mock<ITextToSpeechService>();
            _currentUserMock = new Mock<ICurrentUser>();
            _listeningQuestionGeneratorMock = new Mock<IListeningQuestionGenerator>();

            _responseFactoryMock.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(new { Success = true, Data = data }));
            _responseFactoryMock.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new BadRequestObjectResult(new { Success = false, Message = msg }));

            _controller = new ExamController(
                _responseFactoryMock.Object, _examServiceMock.Object, _ttsServiceMock.Object,
                _currentUserMock.Object, _listeningQuestionGeneratorMock.Object);
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

        // Who may call the listening admin endpoints is [AdminOnly]'s job (AdminOnlyAttributeTests,
        // AdminOnlyEndpointsTests); the action itself only runs for an admin.
        [Fact]
        public async Task GenerateListeningQuestion_CallsGenerator_AsTheSignedInUser()
        {
            _currentUserMock.Setup(x => x.Id).Returns("admin-1");
            _listeningQuestionGeneratorMock.Setup(x => x.GenerateAsync("admin-1"))
                .ReturnsAsync(ServiceResult.Ok(new ListeningTestResponse { Id = "lst-new", ImageUrl = "https://img/lst-new.png", Options = [] }));

            var result = await _controller.GenerateListeningQuestion();

            Assert.IsType<OkObjectResult>(result);
            _listeningQuestionGeneratorMock.Verify(x => x.GenerateAsync("admin-1"), Times.Once);
        }
    }
}
