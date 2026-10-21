using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.DataProviders.US;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// <c>GET api/finance/comprehensive-analysis</c> (TASK-059): a code with no price and no
    /// indicator at all is "not found" rather than a blank success, and a result without a price is
    /// never cached, so an upstream blip can't freeze a stock blank for <c>CACHE_HOURS</c>.
    /// Uses the real <see cref="StockAnalysisBuilder"/> and a real in-memory cache; only the data
    /// providers and the indicator engine are mocked.
    /// </summary>
    public class FinanceQueryServiceComprehensiveAnalysisTests
    {
        private readonly Mock<IMarketDataProvider> _price = new();
        private readonly Mock<IChipDataProvider> _chip = new();
        private readonly Mock<IFundamentalDataProvider> _fundamental = new();
        private readonly Mock<IMarketDataProviderFactory> _factory = new();
        private readonly Mock<IIndicatorEngine> _engine = new();
        private readonly Mock<IStockCodeResolver> _resolver = new();

        public FinanceQueryServiceComprehensiveAnalysisTests()
        {
            _factory.Setup(f => f.DetectRegion(It.IsAny<string>())).Returns(MarketRegion.TW);
            _factory.Setup(f => f.GetChipDataProvider(MarketRegion.TW)).Returns(_chip.Object);
            _resolver.Setup(r => r.ResolveStockCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string code, CancellationToken _) => code);

            _chip.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData());
            _fundamental.Setup(p => p.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FundamentalData?)null);

            // Stand-in for the real calculators: technicals need prices, the PE one needs fundamentals.
            _engine.Setup(e => e.CalculateAll(It.IsAny<IndicatorContext>())).Returns((IndicatorContext c) =>
            {
                var results = new List<IndicatorResult>();
                if (c.Prices.Count > 0)
                    results.Add(new() { Name = "RSI", Category = IndicatorCategory.Technical, Score = 65, Direction = SignalDirection.Bullish, Value = 62, Signal = "偏多", Reason = "RSI=62" });
                if (c.Fundamentals != null)
                    results.Add(new() { Name = "PE", Category = IndicatorCategory.Fundamental, Score = 55, Direction = SignalDirection.Neutral, Value = 20, Signal = "中性", Reason = "PE=20" });
                return results;
            });
        }

        private StockAnalysisQueryService Service()
        {
            var insider = new Mock<SECInsiderTradingProvider>(
                Mock.Of<IHttpClientFactory>(), Mock.Of<IMemoryCache>(), Mock.Of<ILogger<SECInsiderTradingProvider>>());
            var builder = new StockAnalysisBuilder(
                _price.Object, _chip.Object, _fundamental.Object, _factory.Object, _engine.Object,
                new CategoryWeightConfig(), NullLogger<StockAnalysisBuilder>.Instance, insider.Object,
                Mock.Of<IHttpClientFactory>());

            // One service per test class instance, so each test has its own cache; a second call
            // within a test goes through the same cache the first one wrote to.
            return _service ??= new StockAnalysisQueryService(
                NullLogger<StockAnalysisQueryService>.Instance, null!, null!, _resolver.Object, null!,
                new MemoryCache(new MemoryCacheOptions()), null!, null!, null!, null!, builder,
                null!, _factory.Object, null!);
        }

        private StockAnalysisQueryService? _service;

        private static StockAnalysisRequest Request(string code = "2330") => new() { StockCode = code };

        private static MarketData NoData() => new() { StockCode = "9999" };

        private static MarketData WithPrices() => new()
        {
            StockCode = "2330",
            CompanyName = "台積電",
            HistoricalPrices = Enumerable.Range(1, 60).Select(i => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-60 + i), Open = 500 + i, High = 505 + i, Low = 495 + i, Close = 502 + i, Volume = 10000,
            }).ToList(),
        };

        [Fact]
        public async Task UnresolvableTwCode_IsAnExternalError_NotANotFound()
        {
            // The price-bars endpoint answers the same situation with NotFound; this one has always
            // reported it as an upstream failure, and the two share one resolver helper that must keep both.
            _resolver.Setup(r => r.ResolveStockCodeAsync("不存在的公司", It.IsAny<CancellationToken>())).ReturnsAsync("");

            var result = await Service().GetComprehensiveAnalysisAsync(Request("不存在的公司"), default);

            Assert.Equal(ErrorCodes.ExternalApiError, result.Error);
            Assert.Contains("不存在的公司", result.ErrorMessage);
            _price.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExplicitUsMarket_SkipsTheResolver_AndNormalisesTheCode()
        {
            // DetectRegion says TW for everything in this fixture; the request's own market must win,
            // so the builder reads the US provider (a TW request would use the TW one it was built with).
            var us = new Mock<IMarketDataProvider>();
            us.Setup(p => p.FetchAsync("AAPL", It.IsAny<CancellationToken>())).ReturnsAsync(NoData());
            _factory.Setup(f => f.GetMarketDataProvider(MarketRegion.US)).Returns(us.Object);
            _factory.Setup(f => f.GetChipDataProvider(MarketRegion.US)).Returns(_chip.Object);

            var result = await Service().GetComprehensiveAnalysisAsync(
                new StockAnalysisRequest { StockCode = " aapl ", Market = "US" }, default);

            Assert.Equal(ErrorCodes.NotFound, result.Error);   // NoData: the lookup itself ran, for "AAPL"
            Assert.Contains("AAPL", result.ErrorMessage);
            _resolver.VerifyNoOtherCalls();
            us.Verify(p => p.FetchAsync("AAPL", It.IsAny<CancellationToken>()), Times.Once);
            _price.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task NoPriceAndNoIndicator_IsNotFound_AndIsNotCached()
        {
            _price.Setup(p => p.FetchAsync("9999", It.IsAny<CancellationToken>())).ReturnsAsync(NoData());
            var service = Service();

            var first = await service.GetComprehensiveAnalysisAsync(Request("9999"), default);
            var second = await service.GetComprehensiveAnalysisAsync(Request("9999"), default);

            Assert.Equal(ErrorCodes.NotFound, first.Error);
            Assert.Contains("9999", first.ErrorMessage);
            Assert.Equal(ErrorCodes.NotFound, second.Error);
            _price.Verify(p => p.FetchAsync("9999", It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task EmptyFirstThenPrices_RecoversAtOnce_InsteadOfStayingBlank()
        {
            _price.SetupSequence(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(NoData())          // upstream blip
                .ReturnsAsync(WithPrices());     // upstream back
            var service = Service();

            var first = await service.GetComprehensiveAnalysisAsync(Request(), default);
            var second = await service.GetComprehensiveAnalysisAsync(Request(), default);

            Assert.Equal(ErrorCodes.NotFound, first.Error);
            var analysis = Assert.IsType<StockAnalysisResult>(second.Data);
            Assert.True(second.IsSuccess);
            Assert.NotNull(analysis.LatestClose);
            Assert.Equal("台積電", analysis.CompanyName);
            Assert.NotEmpty(analysis.Indicators);
        }

        [Fact]
        public async Task WithPrice_IsCached_SoTheProviderIsNotCalledAgain()
        {
            _price.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>())).ReturnsAsync(WithPrices());
            var service = Service();

            var first = await service.GetComprehensiveAnalysisAsync(Request(), default);
            var second = await service.GetComprehensiveAnalysisAsync(Request(), default);

            var firstResult = Assert.IsType<StockAnalysisResult>(first.Data);
            Assert.Equal("TW", firstResult.Market);
            Assert.Same(firstResult, second.Data);   // the very object the first call cached
            _price.Verify(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task NoPriceButFundamentals_IsReturned_ButNotCached()
        {
            _price.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>())).ReturnsAsync(NoData());
            _fundamental.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundamentalData { PERatio = 20 });
            var service = Service();

            var first = await service.GetComprehensiveAnalysisAsync(Request(), default);
            var second = await service.GetComprehensiveAnalysisAsync(Request(), default);

            var analysis = Assert.IsType<StockAnalysisResult>(first.Data);
            Assert.Null(analysis.LatestClose);
            Assert.Equal("PE", Assert.Single(analysis.Indicators).Name);
            Assert.NotSame(analysis, second.Data);   // rebuilt, not served from cache
            _price.Verify(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
