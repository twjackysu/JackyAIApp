using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestSupport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// The quotes of many TW stocks in one TWStockLib batch, into the cache each stock's own quote is
    /// read from — so a page valuing every holding does not queue one MIS request per stock behind the
    /// library's rate limit.
    /// </summary>
    public class TwQuotePrefetcherTests
    {
        private static readonly TimeSpan Taipei = TimeSpan.FromHours(8);

        private readonly Mock<IStockMarketService> _stocks = new();
        private readonly Mock<ITwListedCompanyService> _listed = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly ManualTimeProvider _clock = new();
        private readonly TwQuotePrefetcher _prefetcher;

        public TwQuotePrefetcherTests()
        {
            _listed.Setup(x => x.GetMarketTypeAsync("2330", It.IsAny<CancellationToken>())).ReturnsAsync(MarketType.TSE);
            _listed.Setup(x => x.GetMarketTypeAsync("6488", It.IsAny<CancellationToken>())).ReturnsAsync(MarketType.OTC);
            _listed.Setup(x => x.GetMarketTypeAsync("0050", It.IsAny<CancellationToken>())).ReturnsAsync(MarketType.TSE);
            _listed.Setup(x => x.GetMarketTypeAsync("9999", It.IsAny<CancellationToken>())).ReturnsAsync((MarketType?)null);
            // Thursday 10:00 in Taipei, mid-session.
            At(new DateTimeOffset(2026, 10, 22, 10, 0, 0, Taipei));
            _prefetcher = new TwQuotePrefetcher(_stocks.Object, _listed.Object, _cache, _clock, NullLogger<TwQuotePrefetcher>.Instance);
        }

        private void At(DateTimeOffset when) => _clock.Advance(when - _clock.GetUtcNow());

        private static StockQuote Quote(string symbol, MarketType market, decimal last) =>
            new() { Symbol = symbol, Name = symbol, Market = market, LastPrice = last };

        private void BatchAnswers(params (string Code, StockResult<StockQuote> Result)[] answers) =>
            _stocks.Setup(x => x.GetRealtimeQuotesAsync(
                    It.IsAny<IEnumerable<(string, MarketType)>>(), It.IsAny<QuoteOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(answers.ToDictionary(a => a.Code, a => a.Result));

        private void VerifyBatch(Times times) =>
            _stocks.Verify(x => x.GetRealtimeQuotesAsync(
                It.IsAny<IEnumerable<(string, MarketType)>>(), It.IsAny<QuoteOptions>(), It.IsAny<CancellationToken>()), times);

        [Fact]
        public async Task TheStocksGoInOneBatch_AndEachStocksQuoteIsThenReadFromTheCache()
        {
            BatchAnswers(
                ("2330", StockResult<StockQuote>.Ok(Quote("2330", MarketType.TSE, 2385m))),
                ("6488", StockResult<StockQuote>.Ok(Quote("6488", MarketType.OTC, 826m))));

            await _prefetcher.PrefetchAsync(["2330", "6488"], CancellationToken.None);

            (string, MarketType)[] each = [("2330", MarketType.TSE), ("6488", MarketType.OTC)];
            _stocks.Verify(x => x.GetRealtimeQuotesAsync(
                It.Is<IEnumerable<(string, MarketType)>>(pairs => pairs.OrderBy(p => p.Item1).SequenceEqual(each)),
                It.IsAny<QuoteOptions>(), It.IsAny<CancellationToken>()), Times.Once);

            // The per-stock path every price fetch takes finds them there.
            var provider = new TWStockLibHistoricalProvider(
                _stocks.Object, _listed.Object, _cache, Mock.Of<IThirdPartyGateway>(),
                NullLogger<TWStockLibHistoricalProvider>.Instance);
            Assert.Equal(2385m, (await provider.GetLatestQuoteAsync("2330", null))!.LastPrice);
            Assert.Equal(826m, (await provider.GetLatestQuoteAsync("6488", null))!.LastPrice);
            _stocks.Verify(x => x.GetRealtimeQuoteAsync(
                It.IsAny<string>(), It.IsAny<MarketType>(), It.IsAny<QuoteOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AStockTheBatchDidNotAnswer_IsNotCached_SoItIsQuotedOnItsOwn()
        {
            BatchAnswers(
                ("2330", StockResult<StockQuote>.Ok(Quote("2330", MarketType.TSE, 2385m))),
                ("6488", StockResult<StockQuote>.Fail(StockErrorCodes.NotFound, "not on this market")));

            await _prefetcher.PrefetchAsync(["2330", "6488"], CancellationToken.None);

            Assert.True(_cache.TryGetValue(CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + "2330", out StockQuote? _));
            Assert.False(_cache.TryGetValue(CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + "6488", out StockQuote? _));
        }

        [Fact]
        public async Task OnlyStocksNotCachedAndOnAKnownMarketAreAsked_AndOneIsNotABatch()
        {
            // 0050 is cached already, 9999's market is unknown: only 2330 is left, one request either way.
            _cache.Set(CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + "0050", Quote("0050", MarketType.TSE, 100m));

            await _prefetcher.PrefetchAsync(["2330", "0050", "9999"], CancellationToken.None);

            VerifyBatch(Times.Never());
        }

        [Theory]
        [InlineData(2026, 10, 22, 8, 59)]  // before the session: the quote still shows yesterday
        [InlineData(2026, 10, 24, 11, 0)]  // Saturday
        public async Task OutsideTheHoursATodaysQuoteIsRead_NothingIsFetched(int y, int m, int d, int h, int min)
        {
            At(new DateTimeOffset(y, m, d, h, min, 0, Taipei));

            await _prefetcher.PrefetchAsync(["2330", "6488"], CancellationToken.None);

            VerifyBatch(Times.Never());
        }

        [Fact]
        public async Task AFailedBatch_OnlyCostsItsTime()
        {
            _stocks.Setup(x => x.GetRealtimeQuotesAsync(
                    It.IsAny<IEnumerable<(string, MarketType)>>(), It.IsAny<QuoteOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("MIS is not answering"));

            await _prefetcher.PrefetchAsync(["2330", "6488"], CancellationToken.None);

            Assert.False(_cache.TryGetValue(CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + "2330", out StockQuote? _));
        }
    }
}
