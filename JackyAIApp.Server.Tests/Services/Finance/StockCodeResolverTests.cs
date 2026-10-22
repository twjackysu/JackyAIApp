using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Both of the resolver's model calls go through <see cref="IOpenAIPromptService"/> with the one
    /// prompt file. A failed call leaves its reason in the log (otherwise every typed company name just
    /// becomes "not found"), and the answer is accepted only as a 4-digit code.
    /// </summary>
    public class StockCodeResolverTests
    {
        private const string CompanyListJson =
            """[{"公司代號":"2330","公司名稱":"台灣積體電路製造股份有限公司"}]""";

        // Not an exact company name, but contained in one: reaches both the "match against the first
        // 100 companies" call and the "match against keyword-filtered candidates" call.
        private const string PartialName = "台灣積體";

        private readonly Mock<IOpenAIPromptService> _promptService = new();
        private readonly RecordingLogger<StockCodeResolver> _logger = new();
        private readonly List<string> _userMessages = [];
        private int _httpRequests;

        private StockCodeResolver Create()
        {
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
                _promptService.Object,
                new MemoryCache(new MemoryCacheOptions()));
        }

        private void SetupAnswer(StockCodeResolution? answer, string? error = null) =>
            _promptService
                .Setup(x => x.GetCompletionAsync<StockCodeResolution>(
                    StockCodeResolver.PromptPath, It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<(string User, string Assistant)>?>(), StockCodeResolver.UsageFeature))
                .Callback<string, string, string?, IEnumerable<(string User, string Assistant)>?, string?>(
                    (_, user, _, _, _) => _userMessages.Add(user))
                .ReturnsAsync((answer, error));

        private void VerifyCalls(Times times) =>
            _promptService.Verify(
                x => x.GetCompletionAsync<StockCodeResolution>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<(string User, string Assistant)>?>(), It.IsAny<string?>()),
                times);

        [Fact]
        public async Task ModelCallFails_ReturnsEmptyAndLogsTheReasonForEachCall()
        {
            SetupAnswer(null, "Unsupported parameter: 'max_completion_tokens'");

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
            var failures = _logger.Entries.Where(e => e.Message.Contains("Stock-code resolution failed")).ToList();
            Assert.Equal(2, failures.Count);
            Assert.All(failures, e =>
            {
                Assert.Equal(LogLevel.Warning, e.Level);
                Assert.Contains("Unsupported parameter: 'max_completion_tokens'", e.Message);
            });
            // Both calls ran exactly once: no retry.
            VerifyCalls(Times.Exactly(2));
        }

        [Fact]
        public async Task ModelAnswersWithStockCode_ReturnsIt()
        {
            SetupAnswer(new StockCodeResolution { StockCode = " 2330 " });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal("2330", result);
            Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
            // The first step answered; the second never ran.
            VerifyCalls(Times.Once());
            Assert.Contains("User input: '台灣積體'", Assert.Single(_userMessages));
            Assert.Contains("2330: 台灣積體電路製造股份有限公司", _userMessages[0]);
        }

        [Fact]
        public async Task ModelFindsNothing_ReturnsEmpty()
        {
            SetupAnswer(new StockCodeResolution { StockCode = null });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
            VerifyCalls(Times.Exactly(2));
        }

        // Input that already looks like a TW code (4-6 digits, optional trailing letter — the same rule
        // MarketDataProviderFactory routes on) is the answer: no company list fetch, no model call.
        [Theory]
        [InlineData("00878", "00878")]
        [InlineData("00919", "00919")]
        [InlineData("006208", "006208")]
        [InlineData("00631L", "00631L")]
        [InlineData("00631l", "00631L")]
        [InlineData("2330", "2330")]
        [InlineData("  2330  ", "2330")]
        public async Task CodeShapedInput_ReturnsNormalizedCodeWithoutHttpOrModel(string input, string expected)
        {
            var result = await Create().ResolveStockCodeAsync(input);

            Assert.Equal(expected, result);
            Assert.Equal(0, _httpRequests);
            VerifyCalls(Times.Never());
        }

        // Looks code-ish but is not a TW code: takes the original path (company list, then the model).
        [Theory]
        [InlineData("123")]       // too short
        [InlineData("1234567")]   // too long
        [InlineData("23A0")]      // letter in the middle
        [InlineData("")]
        public async Task NonCodeShapedInput_TakesTheOriginalNamePath(string input)
        {
            SetupAnswer(new StockCodeResolution { StockCode = null });

            var result = await Create().ResolveStockCodeAsync(input);

            Assert.Equal(string.Empty, result);
            Assert.Equal(1, _httpRequests); // the company list
            VerifyCalls(Times.AtLeastOnce());
        }

        // The relaxed rule is for what the *user typed*. What the model answers stays strictly 4 digits:
        // the company list holds only 4-digit listed companies, so a 5-6 digit answer is a hallucination.
        [Theory]
        [InlineData("00878")]
        [InlineData("006208")]
        [InlineData("00631L")]
        [InlineData("NONE")]
        public async Task ModelAnswersWithAnythingButAFourDigitCode_IsRejected(string answer)
        {
            SetupAnswer(new StockCodeResolution { StockCode = answer });

            var result = await Create().ResolveStockCodeAsync(PartialName);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ThePromptIsEmbedded_AndAsksForJson()
        {
            // OpenAIPromptService requests response_format=json_object, which OpenAI rejects unless
            // the prompt mentions JSON.
            var prompt = new PromptLoader(NullLogger<PromptLoader>.Instance).GetPrompt(StockCodeResolver.PromptPath);

            Assert.NotNull(prompt);
            Assert.Contains("JSON", prompt);
            Assert.Contains("\"StockCode\"", prompt);
        }
    }
}
