using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// What the conversation controller does itself (HTTP concerns). The domain logic is covered by
    /// <see cref="Services.Exam.ConversationPracticeServiceTests"/>.
    /// </summary>
    public class ConversationPracticeControllerTests
    {
        private readonly Mock<IMyResponseFactory> _responseFactoryMock;
        private readonly Mock<IConversationPracticeService> _conversationsMock;
        private readonly ConversationPracticeController _controller;

        public ConversationPracticeControllerTests()
        {
            _responseFactoryMock = new Mock<IMyResponseFactory>();
            _conversationsMock = new Mock<IConversationPracticeService>();

            _responseFactoryMock.Setup(x => x.CreateOKResponse(It.IsAny<object>()))
                .Returns<object>(data => new OkObjectResult(new { Success = true, Data = data }));
            _responseFactoryMock.Setup(x => x.CreateErrorResponse(It.IsAny<ErrorCodes>(), It.IsAny<string>()))
                .Returns<ErrorCodes, string>((code, msg) => new BadRequestObjectResult(new { Success = false, Message = msg }));

            _controller = new ConversationPracticeController(_responseFactoryMock.Object, _conversationsMock.Object);
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
            _conversationsMock.Verify(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
        }
    }
}
