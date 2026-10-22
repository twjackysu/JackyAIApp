using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReasoningEffortType = Betalgo.Ranul.OpenAI.Contracts.Enums.ReasoningEffort;
using ResponseFormatType = Betalgo.Ranul.OpenAI.Contracts.Enums.ResponseFormat;
using AudioResponseFormatType = Betalgo.Ranul.OpenAI.Contracts.Enums.AudioResponseFormat;
using TimestampGranularityType = Betalgo.Ranul.OpenAI.Contracts.Enums.TimestampGranularity;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// Covers the request-shaping fix for "Query failed, OpenAI could not generate ...": without
    /// response_format/max_completion_tokens, the reasoning model (gpt-5-nano) could spend its whole
    /// token budget on invisible reasoning and return empty content for large structured responses
    /// (e.g. a multi-sense dictionary entry), or drift from valid JSON.
    /// </summary>
    public class OpenAIPromptServiceTests
    {
        private record TestDto(string Value);

        private readonly Mock<IOpenAIService> _openAIServiceMock;
        private readonly Mock<IChatCompletionService> _chatCompletionMock;
        private readonly Mock<IAudioService> _audioServiceMock;
        private readonly OpenAIPromptService _service;

        public OpenAIPromptServiceTests()
        {
            _openAIServiceMock = new Mock<IOpenAIService>();
            _chatCompletionMock = new Mock<IChatCompletionService>();
            _audioServiceMock = new Mock<IAudioService>();
            _openAIServiceMock.Setup(x => x.ChatCompletion).Returns(_chatCompletionMock.Object);
            _openAIServiceMock.Setup(x => x.Audio).Returns(_audioServiceMock.Object);

            _service = new OpenAIPromptService(
                _openAIServiceMock.Object,
                new Mock<IPromptLoader>().Object,
                new Mock<IAiUsageLogger>().Object,
                new Mock<ICurrentUser>().Object,
                NullLogger<OpenAIPromptService>.Instance);
        }

        [Fact]
        public async Task GetCompletionWithMessagesAsync_DefaultModel_SetsJsonResponseFormatAndLowReasoningEffort()
        {
            ChatCompletionCreateRequest? captured = null;
            _chatCompletionMock
                .Setup(x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<ChatCompletionCreateRequest, string, CancellationToken>((req, _, _) => captured = req)
                .ReturnsAsync(new ChatCompletionCreateResponse
                {
                    Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("""{"value":"ok"}""") }],
                });

            var (result, error) = await _service.GetCompletionWithMessagesAsync<TestDto>(
                [("user", "hello")]);

            Assert.Null(error);
            Assert.Equal("ok", result!.Value);
            Assert.NotNull(captured);
            Assert.Equal(ResponseFormatType.JsonObject, captured!.ResponseFormat!.Type);
            Assert.Equal(16384, captured.MaxCompletionTokens);
            Assert.Equal(ReasoningEffortType.Low, captured.ReasoningEffort);
        }

        [Fact]
        public async Task GetCompletionWithMessagesAsync_NonReasoningModel_OmitsReasoningEffort()
        {
            ChatCompletionCreateRequest? captured = null;
            _chatCompletionMock
                .Setup(x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<ChatCompletionCreateRequest, string, CancellationToken>((req, _, _) => captured = req)
                .ReturnsAsync(new ChatCompletionCreateResponse
                {
                    Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("""{"value":"ok"}""") }],
                });

            // gpt-4o-mini (used by the conversation exam) is not a reasoning model —
            // the OpenAI API rejects reasoning_effort on it with a 400.
            await _service.GetCompletionWithMessagesAsync<TestDto>([("user", "hello")], "gpt-4o-mini");

            Assert.NotNull(captured);
            Assert.Null(captured!.ReasoningEffort);
            // Still gets the reliability improvements that apply to every model.
            Assert.Equal(ResponseFormatType.JsonObject, captured.ResponseFormat!.Type);
            Assert.Equal(16384, captured.MaxCompletionTokens);
        }

        [Fact]
        public async Task GetCompletionWithMessagesAsync_Gpt6Luna_SetsLowReasoningEffort()
        {
            ChatCompletionCreateRequest? captured = null;
            _chatCompletionMock
                .Setup(x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<ChatCompletionCreateRequest, string, CancellationToken>((req, _, _) => captured = req)
                .ReturnsAsync(new ChatCompletionCreateResponse
                {
                    Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("""{"value":"ok"}""") }],
                });

            // gpt-6-luna is reasoning-family and defaults to medium effort; these prompts don't need it.
            await _service.GetCompletionWithMessagesAsync<TestDto>([("user", "hello")], NewModels.GPT_6_LUNA);

            Assert.NotNull(captured);
            Assert.Equal(ReasoningEffortType.Low, captured!.ReasoningEffort);
            Assert.Equal(16384, captured.MaxCompletionTokens);
        }

        [Fact]
        public async Task GetCompletionWithMessagesAsync_EmptyContent_ReturnsDescriptiveError()
        {
            _chatCompletionMock
                .Setup(x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatCompletionCreateResponse
                {
                    Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("") }],
                });

            var (result, error) = await _service.GetCompletionWithMessagesAsync<TestDto>([("user", "hello")]);

            Assert.Null(result);
            Assert.Equal("OpenAI returned empty response", error);
        }

        [Fact]
        public async Task TranscribeAudioAsync_RequestsVerboseJsonWithWordTimestamps()
        {
            AudioCreateTranscriptionRequest? captured = null;
            _audioServiceMock
                .Setup(x => x.CreateTranscription(It.IsAny<AudioCreateTranscriptionRequest>(), It.IsAny<CancellationToken>()))
                .Callback<AudioCreateTranscriptionRequest, CancellationToken>((req, _) => captured = req)
                .ReturnsAsync(new AudioCreateTranscriptionResponse
                {
                    Text = "hello world",
                    Duration = 3.5f,
                    Words = [new AudioCreateTranscriptionResponse.WordSegment { Word = "hello", Start = 0f, End = 1f }]
                });

            using var stream = new MemoryStream([1, 2, 3]);
            var (result, error) = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.Null(error);
            Assert.NotNull(result);
            Assert.Equal("hello world", result!.Text);
            Assert.Equal(3.5, result.DurationSeconds);
            Assert.Single(result.Words);
            Assert.Equal("hello", result.Words[0].Word);

            Assert.NotNull(captured);
            Assert.Equal(AudioResponseFormatType.VerboseJson, captured!.ResponseFormat);
            Assert.Equal([TimestampGranularityType.Word], captured.TimestampGranularities);
        }

        [Fact]
        public async Task TranscribeAudioAsync_FailedResponse_ReturnsError()
        {
            _audioServiceMock
                .Setup(x => x.CreateTranscription(It.IsAny<AudioCreateTranscriptionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudioCreateTranscriptionResponse
                {
                    // Error.Message has a private setter; MessageObject is the public setter that
                    // populates it (see Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels.Error).
                    Error = new Error { MessageObject = "boom" }
                });

            using var stream = new MemoryStream([1, 2, 3]);
            var (result, error) = await _service.TranscribeAudioAsync(stream, "test.mp3");

            Assert.Null(result);
            Assert.Equal("boom", error);
        }
    }
}
