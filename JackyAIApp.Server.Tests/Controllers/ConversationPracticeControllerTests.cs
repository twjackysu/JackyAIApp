using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Reflection;

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

        [Fact]
        public async Task TranscribeAudio_RecordingOverTheLimit_IsRefusedBeforeWhisper()
        {
            var file = new Mock<IFormFile>();
            file.Setup(f => f.Length).Returns(ConversationPracticeController.MaxAudioBytes + 1L);
            file.Setup(f => f.FileName).Returns("recording.wav");

            await _controller.TranscribeAudio(file.Object);

            _responseFactoryMock.Verify(x => x.CreateErrorResponse(
                ErrorCodes.BadRequest,
                "The recording is too long."), Times.Once);
            _conversationsMock.Verify(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
            file.Verify(f => f.OpenReadStream(), Times.Never);
        }

        [Fact]
        public async Task TranscribeAudio_RecordingAtTheLimit_IsTranscribed()
        {
            var file = new Mock<IFormFile>();
            file.Setup(f => f.Length).Returns(ConversationPracticeController.MaxAudioBytes);
            file.Setup(f => f.FileName).Returns("recording.wav");
            file.Setup(f => f.OpenReadStream()).Returns(new MemoryStream([1, 2, 3]));
            _conversationsMock.Setup(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), "recording.wav"))
                .ReturnsAsync(ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Failed to transcribe audio."));

            await _controller.TranscribeAudio(file.Object);

            _conversationsMock.Verify(x => x.TranscribeAudioAsync(It.IsAny<Stream>(), "recording.wav"), Times.Once);
        }

        [Fact]
        public void TranscribeAudio_TheRequestBodyIsLimited()
        {
            // Without it Kestrel would read up to its default (about 28 MB) before the action can refuse.
            var limit = typeof(ConversationPracticeController)
                .GetMethod(nameof(ConversationPracticeController.TranscribeAudio))!
                .GetCustomAttribute<RequestSizeLimitAttribute>();

            Assert.NotNull(limit);
            // Two minutes at 64 kbps (the client's cap) is about 1 MB.
            Assert.Equal(4 * 1024 * 1024, ConversationPracticeController.MaxAudioBytes);
        }
    }
}
