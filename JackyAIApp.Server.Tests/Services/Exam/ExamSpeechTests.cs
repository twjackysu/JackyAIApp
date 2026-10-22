using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Exam;
using JackyAIApp.Server.Services.Speech;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>
    /// The exams' spoken audio (<see cref="ExamSpeech"/>): one voice (alloy, tts-1, normal speed),
    /// the cache before the provider, and a provider failure reported as such.
    /// </summary>
    public class ExamSpeechTests
    {
        private readonly Mock<ITextToSpeechService> _tts = new();
        private readonly ExamSpeech _speech;

        public ExamSpeechTests()
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns("alice");
            _speech = new ExamSpeech(_tts.Object, user.Object);
        }

        private void Cached(byte[]? audio)
        {
            _tts.Setup(t => t.TryGetCached("apple", "alloy", Models.Tts_1, 1.0f, out audio)).Returns(audio != null);
        }

        [Fact]
        public async Task ACachedClip_IsServed_WithoutTheProvider()
        {
            Cached([1, 2, 3]);

            var result = await _speech.SpeakAsync("apple");

            Assert.Equal(new byte[] { 1, 2, 3 }, result.Data);
            _tts.Verify(t => t.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task OtherwiseItIsSynthesized_InTheExamVoice_ForTheUser()
        {
            Cached(null);
            _tts.Setup(t => t.SynthesizeAsync("apple", "alloy", Models.Tts_1, 1.0f, "alice"))
                .ReturnsAsync(new TtsResult(true, [4, 5]));

            var result = await _speech.SpeakAsync("apple");

            Assert.Equal(new byte[] { 4, 5 }, result.Data);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]  // reported failed, whatever came back
        [InlineData(true, false)]  // reported fine, but no audio
        public async Task AProviderFailure_IsAnExternalApiError(bool success, bool hasAudio)
        {
            Cached(null);
            _tts.Setup(t => t.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<string?>()))
                .ReturnsAsync(new TtsResult(success, hasAudio ? [9] : null));

            var result = await _speech.SpeakAsync("apple");

            Assert.Equal(ErrorCodes.ExternalApiError, result.Error);
        }
    }
}
