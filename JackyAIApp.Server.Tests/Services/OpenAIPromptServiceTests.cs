using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ReasoningEffortType = Betalgo.Ranul.OpenAI.Contracts.Enums.ReasoningEffort;
using ResponseFormatType = Betalgo.Ranul.OpenAI.Contracts.Enums.ResponseFormat;

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
        private readonly OpenAIPromptService _service;

        public OpenAIPromptServiceTests()
        {
            _openAIServiceMock = new Mock<IOpenAIService>();
            _chatCompletionMock = new Mock<IChatCompletionService>();
            _openAIServiceMock.Setup(x => x.ChatCompletion).Returns(_chatCompletionMock.Object);

            _service = new OpenAIPromptService(
                _openAIServiceMock.Object,
                new Mock<IPromptLoader>().Object,
                new Mock<IAiUsageLogger>().Object,
                new Mock<IUserService>().Object,
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
            Assert.Equal(4096, captured.MaxCompletionTokens);
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

            // gpt-4.1-nano (used by Finance/Portfolio AI summaries) is not a reasoning model —
            // the OpenAI API rejects reasoning_effort on it with a 400.
            await _service.GetCompletionWithMessagesAsync<TestDto>([("user", "hello")], "gpt-4.1-nano");

            Assert.NotNull(captured);
            Assert.Null(captured!.ReasoningEffort);
            // Still gets the reliability improvements that apply to every model.
            Assert.Equal(ResponseFormatType.JsonObject, captured.ResponseFormat!.Type);
            Assert.Equal(4096, captured.MaxCompletionTokens);
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
    }
}
