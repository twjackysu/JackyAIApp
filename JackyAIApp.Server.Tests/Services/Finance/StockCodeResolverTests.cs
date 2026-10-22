using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Names are looked up in the TW symbol list first (上市 and 上櫃, short names, ETFs); the model is
    /// asked only when that does not settle it, through <see cref="IOpenAIPromptService"/> with the one
    /// prompt file, and its answer is taken only if the code is on the list. A failed call leaves its
    /// reason in the log (otherwise every typed company name just becomes "not found").
    /// </summary>
    public class StockCodeResolverTests
    {
        /// <summary>Searches the way <see cref="TwListedCompanyService"/> does: code prefix or name
        /// contains, code-prefix matches first.</summary>
        private sealed class FakeListedCompanies(params (string Symbol, string Name, MarketType Market)[] list) : ITwListedCompanyService
        {
            public int Lookups { get; private set; }

            public Task<List<StockSearchItem>> SearchAsync(string keyword, CancellationToken ct = default)
            {
                Lookups++;
                return Task.FromResult(list
                    .Where(c => c.Symbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                        || c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Symbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(c => c.Symbol, StringComparer.Ordinal)
                    .Select(c => new StockSearchItem { Symbol = c.Symbol, Name = c.Name, Market = "TW" })
                    .ToList());
            }

            public Task<MarketType?> GetMarketTypeAsync(string symbol, CancellationToken ct = default)
            {
                Lookups++;
                return Task.FromResult(list.Where(c => c.Symbol == symbol).Select(c => (MarketType?)c.Market).FirstOrDefault());
            }

            public Task<bool> RefreshAsync(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<DateTime?> GetLastRefreshedAtAsync(CancellationToken ct = default) => throw new NotSupportedException();
        }

        private readonly FakeListedCompanies _listed = new(
            ("2330", "台積電", MarketType.TSE),
            ("2882", "國泰金", MarketType.TSE),
            ("00878", "國泰永續高股息", MarketType.TSE),
            ("6488", "環球晶", MarketType.OTC),
            ("2603", "長榮", MarketType.TSE),
            ("2618", "長榮航", MarketType.TSE));

        private readonly Mock<IOpenAIPromptService> _promptService = new();
        private readonly RecordingLogger<StockCodeResolver> _logger = new();
        private readonly List<string> _userMessages = [];

        private StockCodeResolver Create() => new(_logger, _promptService.Object, _listed);

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

        // Input that already looks like a TW code (4-6 digits, optional trailing letter — the same rule
        // MarketDataProviderFactory routes on) is the answer: no lookup, no model call.
        [Theory]
        [InlineData("00878", "00878")]
        [InlineData("006208", "006208")]
        [InlineData("00631l", "00631L")]
        [InlineData("  2330  ", "2330")]
        public async Task CodeShapedInput_ReturnsNormalizedCode_WithoutLookupOrModel(string input, string expected)
        {
            Assert.Equal(expected, await Create().ResolveStockCodeAsync(input));
            Assert.Equal(0, _listed.Lookups);
            VerifyCalls(Times.Never());
        }

        [Theory]
        [InlineData("台積電", "2330")]
        [InlineData("環球晶", "6488")]          // 上櫃: not in TWSE's company list, which the model had to guess
        [InlineData("國泰永續高股息", "00878")]  // an ETF
        [InlineData("長榮", "2603")]            // also part of 長榮航's name
        public async Task AListedName_IsItsCode_WithoutTheModel(string input, string expected)
        {
            Assert.Equal(expected, await Create().ResolveStockCodeAsync(input));
            VerifyCalls(Times.Never());
        }

        [Fact]
        public async Task OneListedNameContainingTheInput_IsItsCode_WithoutTheModel()
        {
            Assert.Equal("2330", await Create().ResolveStockCodeAsync("台積"));
            VerifyCalls(Times.Never());
        }

        [Fact]
        public async Task SeveralListedNamesContainingTheInput_TheModelPicksAmongThem()
        {
            SetupAnswer(new StockCodeResolution { StockCode = "2882" });

            Assert.Equal("2882", await Create().ResolveStockCodeAsync("國泰"));
            var message = Assert.Single(_userMessages);
            Assert.Contains("User input: '國泰'", message);
            Assert.Contains("00878: 國泰永續高股息\n2882: 國泰金", message);
        }

        [Fact]
        public async Task NoListedNameContainingTheInput_TheModelAnswersFromItsOwnKnowledge()
        {
            SetupAnswer(new StockCodeResolution { StockCode = " 2330 " });

            Assert.Equal("2330", await Create().ResolveStockCodeAsync("TSMC"));
            Assert.Contains("(none of the listed names contains the input)", Assert.Single(_userMessages));
            Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
        }

        [Fact]
        public async Task TheModelNamingAListedEtf_IsTaken()
        {
            SetupAnswer(new StockCodeResolution { StockCode = "00878" });

            Assert.Equal("00878", await Create().ResolveStockCodeAsync("Cathay ESG high dividend"));
        }

        [Theory]
        [InlineData("9999")]   // shaped like a code, but nothing listed has it
        [InlineData("NONE")]
        [InlineData("23A0")]
        public async Task TheModelNamingAnythingButAListedCode_IsRejected(string answer)
        {
            SetupAnswer(new StockCodeResolution { StockCode = answer });

            Assert.Equal(string.Empty, await Create().ResolveStockCodeAsync("TSMC"));
        }

        [Fact]
        public async Task ModelFindsNothing_ReturnsEmpty()
        {
            SetupAnswer(new StockCodeResolution { StockCode = null });

            Assert.Equal(string.Empty, await Create().ResolveStockCodeAsync("TSMC"));
            VerifyCalls(Times.Once());
        }

        [Fact]
        public async Task ModelCallFails_ReturnsEmptyAndLogsTheReason()
        {
            SetupAnswer(null, "Unsupported parameter: 'max_completion_tokens'");

            Assert.Equal(string.Empty, await Create().ResolveStockCodeAsync("TSMC"));
            var failure = Assert.Single(_logger.Entries, e => e.Message.Contains("Stock-code resolution failed"));
            Assert.Equal(LogLevel.Warning, failure.Level);
            Assert.Contains("Unsupported parameter: 'max_completion_tokens'", failure.Message);
            VerifyCalls(Times.Once()); // no retry
        }

        // Looks code-ish but is not a TW code: looked up as a name.
        [Theory]
        [InlineData("123")]       // too short
        [InlineData("1234567")]   // too long
        public async Task NonCodeShapedInput_IsLookedUpAsAName(string input)
        {
            SetupAnswer(new StockCodeResolution { StockCode = null });

            Assert.Equal(string.Empty, await Create().ResolveStockCodeAsync(input));
            Assert.True(_listed.Lookups > 0);
        }

        [Fact]
        public async Task NoInput_IsNoCode()
        {
            Assert.Equal(string.Empty, await Create().ResolveStockCodeAsync("  "));
            VerifyCalls(Times.Never());
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
