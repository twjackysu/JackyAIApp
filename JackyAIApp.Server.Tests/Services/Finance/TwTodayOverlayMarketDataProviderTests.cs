using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// Covers the outermost TW <see cref="IMarketDataProvider"/> decorator that overlays today's
    /// price — moved here from PortfolioService so every caller of <c>IMarketDataProvider</c> (not
    /// just Portfolio) gets it; see the class's own docs for why it must sit outside
    /// CachedMarketDataProvider's cache. The time-of-day gate went through several wrong
    /// assumptions in conversation before landing on "09:00 onward, no upper bound" — these tests
    /// exist specifically so that gate doesn't regress silently.
    /// </summary>
    public class TwTodayOverlayMarketDataProviderTests
    {
        private const string Symbol = "2330";

        private static readonly MarketData YesterdayOnly = new()
        {
            StockCode = Symbol,
            HistoricalPrices = [new DailyPrice { Date = DateTime.Today.AddDays(-1), Close = 2400m }],
        };

        private static StockQuote MakeQuote(decimal lastPrice) => new()
        {
            Symbol = Symbol,
            Name = "台積電",
            Market = MarketType.TSE,
            LastPrice = lastPrice,
            OpeningPrice = lastPrice,
            HighestPrice = lastPrice,
            LowestPrice = lastPrice,
        };

        private static StockQuote MakeQuoteWithYesterdayClose(decimal yesterdayClose) => new()
        {
            Symbol = Symbol,
            Name = "台積電",
            Market = MarketType.TSE,
            YesterdayClosingPrice = yesterdayClose,
        };

        private static (Mock<IMarketDataProvider> inner, Mock<ITWStockLibHistoricalProvider> quoteSource, TwTodayOverlayMarketDataProvider provider)
            Build(DateTimeOffset now)
        {
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(YesterdayOnly);
            inner.Setup(p => p.FetchAsync(Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(YesterdayOnly);
            var quoteSource = new Mock<ITWStockLibHistoricalProvider>();
            var timeProvider = new FakeTimeProvider(now);
            var provider = new TwTodayOverlayMarketDataProvider(
                inner.Object, quoteSource.Object, NullLogger<TwTodayOverlayMarketDataProvider>.Instance, timeProvider);
            return (inner, quoteSource, provider);
        }

        // Wed 2026-07-22 (a known weekday), Taipei = UTC+8.
        private static DateTimeOffset TaipeiTime(int hour, int minute = 0) =>
            new DateTimeOffset(2026, 7, 22, hour, minute, 0, TimeSpan.Zero).AddHours(-8);

        [Fact]
        public async Task FetchLatestAsync_DuringWindow_OverlaysTodayQuote()
        {
            var (_, quoteSource, provider) = Build(TaipeiTime(10, 30));
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeQuote(2450m));

            var result = await provider.FetchLatestAsync(Symbol);

            var today = Assert.Single(result.HistoricalPrices, p => p.Date.Date == DateTime.Today);
            Assert.Equal(2450m, today.Close);
            // Yesterday's bar from the inner chain is preserved, not dropped.
            Assert.Contains(result.HistoricalPrices, p => p.Date.Date == DateTime.Today.AddDays(-1) && p.Close == 2400m);
        }

        [Fact]
        public async Task FetchLatestAsync_WellAfterMarketClose_StillOverlaysTodayQuote()
        {
            // The whole point of this fix: no upper-bound cutoff at 13:30 close.
            var (_, quoteSource, provider) = Build(TaipeiTime(20, 0));
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeQuote(2350m));

            var result = await provider.FetchLatestAsync(Symbol);

            var today = Assert.Single(result.HistoricalPrices, p => p.Date.Date == DateTime.Today);
            Assert.Equal(2350m, today.Close);
        }

        [Fact]
        public async Task FetchLatestAsync_BeforeMarketOpen_DoesNotOverlayToday()
        {
            var (_, quoteSource, provider) = Build(TaipeiTime(8, 59));
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeQuote(2450m));

            var result = await provider.FetchLatestAsync(Symbol);

            Assert.DoesNotContain(result.HistoricalPrices, p => p.Close == 2450m);
        }

        [Fact]
        public async Task FetchLatestAsync_BeforeMarketOpen_BackfillsMissingYesterdayClose()
        {
            // DB-backed chain only has 2026-07-20 (Monday) — missing 2026-07-21 (Tuesday), the
            // last trading day before the fixed "now" of Wed 2026-07-22 07:30. This is the exact
            // bug scenario: closing data not yet ingested by the next morning.
            var stale = new MarketData
            {
                StockCode = Symbol,
                HistoricalPrices = [new DailyPrice { Date = new DateTime(2026, 7, 20), Close = 2380m }],
            };
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(stale);
            var quoteSource = new Mock<ITWStockLibHistoricalProvider>();
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeQuoteWithYesterdayClose(2410m));
            var provider = new TwTodayOverlayMarketDataProvider(
                inner.Object, quoteSource.Object, NullLogger<TwTodayOverlayMarketDataProvider>.Instance,
                new FakeTimeProvider(TaipeiTime(7, 30)));

            var result = await provider.FetchLatestAsync(Symbol);

            var backfilled = Assert.Single(result.HistoricalPrices, p => p.Date == new DateTime(2026, 7, 21));
            Assert.Equal(2410m, backfilled.Close);
            // Existing bar preserved, and no "today" (07-22) bar since market hasn't opened yet.
            Assert.Contains(result.HistoricalPrices, p => p.Date == new DateTime(2026, 7, 20) && p.Close == 2380m);
            Assert.DoesNotContain(result.HistoricalPrices, p => p.Date == new DateTime(2026, 7, 22));
        }

        [Fact]
        public async Task FetchLatestAsync_DbAlreadyHasYesterday_DoesNotBackfill()
        {
            // 2026-07-21 (Tuesday) is already the last trading day before Wed 2026-07-22 — no gap.
            var upToDate = new MarketData
            {
                StockCode = Symbol,
                HistoricalPrices = [new DailyPrice { Date = new DateTime(2026, 7, 21), Close = 2400m }],
            };
            var inner = new Mock<IMarketDataProvider>();
            inner.Setup(p => p.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>())).ReturnsAsync(upToDate);
            var quoteSource = new Mock<ITWStockLibHistoricalProvider>();
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeQuoteWithYesterdayClose(2410m));
            var provider = new TwTodayOverlayMarketDataProvider(
                inner.Object, quoteSource.Object, NullLogger<TwTodayOverlayMarketDataProvider>.Instance,
                new FakeTimeProvider(TaipeiTime(7, 30)));

            var result = await provider.FetchLatestAsync(Symbol);

            var bar = Assert.Single(result.HistoricalPrices);
            Assert.Equal(2400m, bar.Close); // untouched, not overwritten by the quote's 2410
        }

        [Fact]
        public async Task FetchLatestAsync_Weekend_DoesNotFetchQuoteOrOverlay()
        {
            // 2026-07-25 is a Saturday.
            var saturday = new DateTimeOffset(2026, 7, 25, 10, 0, 0, TimeSpan.Zero).AddHours(-8);
            var (_, quoteSource, provider) = Build(saturday);

            var result = await provider.FetchLatestAsync(Symbol);

            Assert.Same(YesterdayOnly, result);
            quoteSource.Verify(
                q => q.GetLatestQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task FetchLatestAsync_QuoteFetchThrows_FallsBackToInnerData()
        {
            var (_, quoteSource, provider) = Build(TaipeiTime(10, 30));
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("boom"));

            var result = await provider.FetchLatestAsync(Symbol);

            Assert.Same(YesterdayOnly, result);
        }

        [Fact]
        public async Task FetchLatestAsync_QuoteHasNoUsablePrice_FallsBackToInnerData()
        {
            var (_, quoteSource, provider) = Build(TaipeiTime(10, 30));
            quoteSource.Setup(q => q.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                // no LastPrice, no bid/ask — no usable price at all
                .ReturnsAsync(new StockQuote { Symbol = Symbol, Name = "台積電", Market = MarketType.TSE });

            var result = await provider.FetchLatestAsync(Symbol);

            Assert.Same(YesterdayOnly, result);
        }

        [Fact]
        public async Task InvalidateAsync_DelegatesToInner()
        {
            var (inner, _, provider) = Build(TaipeiTime(10, 30));
            inner.Setup(p => p.InvalidateAsync(Symbol, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await provider.InvalidateAsync(Symbol);

            inner.Verify(p => p.InvalidateAsync(Symbol, It.IsAny<CancellationToken>()), Times.Once);
        }

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
