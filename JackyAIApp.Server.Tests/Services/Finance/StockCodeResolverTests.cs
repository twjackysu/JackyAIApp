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
        private readonly Mock<IAiUsageLogger> _usageLogger = new();
        private readonly RecordingLogger<StockCodeResolver> _logger = new();

        private StockCodeResolver Create()
        {
            var openAI = new Mock<IOpenAIService>();
            openAI.Setup(x => x.ChatCompletion).Returns(_chatCompletion.Object);

            var httpClient = StubHttpMessageHandler.CreateClient(_ => StubHttpMessageHandler.JsonResponse(CompanyListJson));
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            return new StockCodeResolver(
                httpClientFactory.Object,
                _logger,
                openAI.Object,
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
    }
}
