using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Listening;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// What the listening controller does itself: answering an option with its audio (the lookup's
    /// or the speech's failure passed on), and generating a question as the signed-in admin. Who may
    /// call the admin endpoints is [AdminOnly]'s job (AdminOnlyAttributeTests, AdminOnlyEndpointsTests).
    /// </summary>
    public class ListeningExamControllerTests
    {
        private readonly Mock<IMyResponseFactory> _responseFactory = new();
        private readonly Mock<IExamSpeech> _speech = new();
        private readonly Mock<IListeningExamService> _listening = new();
        private readonly Mock<IListeningQuestionGenerator> _generator = new();
        private readonly Mock<ICurrentUser> _currentUser = new();
        private readonly ListeningExamController _controller;

        public ListeningExamControllerTests()
        {
            _responseFactory.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(data));
            _responseFactory.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new ObjectResult(msg) { StatusCode = (int)code });
            _controller = new ListeningExamController(
                _responseFactory.Object, _speech.Object, _listening.Object, _generator.Object, _currentUser.Object);
        }

        [Fact]
        public async Task OptionAudio_IsTheSpokenOptionText()
        {
            _listening.Setup(l => l.GetOptionTextAsync("opt-1")).ReturnsAsync(ServiceResult.Ok("A man is pointing."));
            _speech.Setup(s => s.SpeakAsync("A man is pointing.")).ReturnsAsync(ServiceResult.Ok(new byte[] { 1, 2 }));

            var file = Assert.IsType<FileContentResult>(await _controller.GetListeningOptionAudio("opt-1"));

            Assert.Equal("audio/mpeg", file.ContentType);
            Assert.Equal(new byte[] { 1, 2 }, file.FileContents);
        }

        [Fact]
        public async Task OptionAudio_ForAnUnknownOption_IsNotFound_AndNothingIsSpoken()
        {
            _listening.Setup(l => l.GetOptionTextAsync("nope"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.NotFound, "Listening question option not found."));

            await _controller.GetListeningOptionAudio("nope");

            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.NotFound, "Listening question option not found."), Times.Once);
            _speech.Verify(s => s.SpeakAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task OptionAudio_WhenSpeechFails_PassesTheFailureOn()
        {
            _listening.Setup(l => l.GetOptionTextAsync("opt-1")).ReturnsAsync(ServiceResult.Ok("text"));
            _speech.Setup(s => s.SpeakAsync("text"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.ExternalApiError, "Failed to generate speech"));

            await _controller.GetListeningOptionAudio("opt-1");

            _responseFactory.Verify(f => f.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to generate speech"), Times.Once);
        }

        [Fact]
        public async Task GenerateListeningQuestion_CallsGenerator_AsTheSignedInUser()
        {
            _currentUser.Setup(x => x.Id).Returns("admin-1");
            _generator.Setup(x => x.GenerateAsync("admin-1"))
                .ReturnsAsync(ServiceResult.Ok(new ListeningTestResponse { Id = "lst-new", ImageUrl = "https://img/lst-new.png", Options = [] }));

            var result = await _controller.GenerateListeningQuestion();

            Assert.IsType<OkObjectResult>(result);
            _generator.Verify(x => x.GenerateAsync("admin-1"), Times.Once);
        }
    }
}
