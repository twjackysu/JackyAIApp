using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.AiUsage;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// A request OpenAI rejects outright (e.g. HTTP 400 on a parameter the model does not accept)
    /// carries no <c>Usage</c>, so no AiUsageLogs row is written, and the resolver itself returned an
    /// empty string with nothing in the log: every typed company name just became "not found".
    /// These tests pin that both OpenAI calls leave the reason in the log while the behaviour
    /// (empty result, no throw, no retry) stays the same.
    /// </summary>
    public class StockCodeResolverTests
    {
        private const string CompanyListJson =
            """[{"公司代號":"2330","公司名稱":"台灣積體電路製造股份有限公司"}]""";

        // Not an exact company name, but contained in one: reaches both the "match against the first
        // 100 companies" call and the "match against keyword-filtered candidates" call.
        private const string PartialName = "台灣積體";

        private readonly Mock<IChatCompletionService> _chatCompletion = new();
        private readonly Mock<IOpenAIService> _openAI = new();
        private readonly Mock<IAiUsageLogger> _usageLogger = new();
        private readonly RecordingLogger<StockCodeResolver> _logger = new();
        private int _httpRequests;

        private StockCodeResolver Create()
        {
            _openAI.Setup(x => x.ChatCompletion).Returns(_chatCompletion.Object);

            var httpClient = StubHttpMessageHandler.CreateClient(_ =>
            {
                Interlocked.Increment(ref _httpRequests);
                return StubHttpMessageHandler.JsonResponse(CompanyListJson);
            });
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            return new StockCodeResolver(
                httpClientFactory.Object,
                _logger,
                _openAI.Object,
                new MemoryCache(new MemoryCacheOptions()),
                _usageLogger.Object,
                new Mock<IUserService>().Object);
        }

        private void SetupCompletion(ChatCompletionCreateResponse response) =>
            _chatCompletion
                .Setup(x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

        [Fact]
        public async Task OpenAIRejectsRequest_ReturnsEmptyAndLogsErrorForEachCall()
        {
            SetupCompletion(new ChatCompletionCreateResponse
            {
                Error = new Error { MessageObject = "Unsupported parameter: 'max_completion_tokens'" },
            });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
            var failures = _logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
            Assert.Equal(2, failures.Count);
            Assert.All(failures, e =>
            {
                Assert.Contains("OpenAI stock-code resolution failed", e.Message);
                Assert.Contains("Unsupported parameter: 'max_completion_tokens'", e.Message);
            });
            // Both calls ran exactly once: no retry.
            _chatCompletion.Verify(
                x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task OpenAIRejectsRequestWithoutErrorMessage_LogsGenericMessage()
        {
            SetupCompletion(new ChatCompletionCreateResponse { Error = new Error() });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
            var failures = _logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
            Assert.NotEmpty(failures);
            Assert.All(failures, e => Assert.Contains("OpenAI request failed", e.Message));
        }

        [Fact]
        public async Task OpenAIAnswersWithStockCode_ReturnsItWithoutErrorLog()
        {
            SetupCompletion(new ChatCompletionCreateResponse
            {
                Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("2330") }],
            });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal("2330", result);
            Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Error);
        }

        // Input that already looks like a TW code (4-6 digits, optional trailing letter — the same rule
        // MarketDataProviderFactory routes on) is the answer: no company list fetch, no LLM call.
        [Theory]
        [InlineData("00878", "00878")]
        [InlineData("00919", "00919")]
        [InlineData("006208", "006208")]
        [InlineData("00631L", "00631L")]
        [InlineData("00631l", "00631L")]
        [InlineData("2330", "2330")]
        [InlineData("  2330  ", "2330")]
        public async Task CodeShapedInput_ReturnsNormalizedCodeWithoutHttpOrOpenAI(string input, string expected)
        {
            var result = await Create().ResolveStockCodeAsync(input);

            Assert.Equal(expected, result);
            Assert.Equal(0, _httpRequests);
            _openAI.Verify(x => x.ChatCompletion, Times.Never);
            _chatCompletion.Verify(
                x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _usageLogger.VerifyNoOtherCalls();
        }

        // Looks code-ish but is not a TW code: takes the original path (company list, then the LLM).
        [Theory]
        [InlineData("123")]       // too short
        [InlineData("1234567")]   // too long
        [InlineData("23A0")]      // letter in the middle
        [InlineData("")]
        public async Task NonCodeShapedInput_TakesTheOriginalNamePath(string input)
        {
            SetupCompletion(new ChatCompletionCreateResponse
            {
                Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant("NONE") }],
            });

            var result = await Create().ResolveStockCodeAsync(input);

            Assert.Equal(string.Empty, result);
            Assert.Equal(1, _httpRequests); // the company list
            _chatCompletion.Verify(
                x => x.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        // The relaxed rule is for what the *user typed*. What the LLM answers stays strictly 4 digits:
        // the company list holds only 4-digit listed companies, so a 5-6 digit answer is a hallucination.
        [Theory]
        [InlineData("00878")]
        [InlineData("006208")]
        [InlineData("00631L")]
        public async Task OpenAIAnswersWithNonFourDigitCode_IsRejected(string answer)
        {
            SetupCompletion(new ChatCompletionCreateResponse
            {
                Choices = [new ChatChoiceResponse { Message = ChatMessage.FromAssistant(answer) }],
            });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
        }
    }
}
