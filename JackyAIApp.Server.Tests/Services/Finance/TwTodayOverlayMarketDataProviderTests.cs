using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.Extensions.Logging;
using Moq;
using TWStockLib.Models;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// After 13:30 the realtime quote is the only thing that surfaces today's TW close — the
    /// persistent layer deliberately never holds today's bar. So when the quote is unavailable the
    /// app serves yesterday's price, which looks like a working page rather than a failure.
    ///
    /// These tests pin that the degradation is reported. It previously was not: the exception path
    /// logged at debug (production runs at Information, so it never appeared) and the more likely
    /// path — a quote that comes back null because the response did not parse — logged nothing at
    /// all. A stale price with no log is indistinguishable from a correct one.
    /// </summary>
    public class TwTodayOverlayMarketDataProviderTests
    {
        private const string Symbol = "0050";

        // A weekday afternoon in Taipei: past 09:00, so the overlay is in its active window.
        private static readonly DateTimeOffset FridayAfternoon =
            new(2026, 9, 11, 9, 21, 0, TimeSpan.Zero); // 17:21 Taipei

        private readonly Mock<IMarketDataProvider> _inner = new();
        private readonly Mock<ITWStockLibHistoricalProvider> _quotes = new();
        private readonly Mock<ILogger<TwTodayOverlayMarketDataProvider>> _logger = new();

        public TwTodayOverlayMarketDataProviderTests()
        {
            _inner.Setup(x => x.FetchLatestAsync(Symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(HistoricalOnly());
            _inner.Setup(x => x.FetchAsync(Symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(HistoricalOnly());
        }

        [Fact]
        public async Task QuoteMissing_WarnsThatTheHistoricalCloseIsBeingServed()
        {
            // The shape the reported incident took: a non-JSON response parses to null rather than
            // throwing, so nothing was logged and the page quietly showed yesterday.
            _quotes.Setup(x => x.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockQuote?)null);

            var result = await Create().FetchLatestAsync(Symbol);

            Assert.Equal(109.15m, result.HistoricalPrices[^1].Close); // unchanged fallback
            AssertLogged(LogLevel.Warning);
        }

        [Fact]
        public async Task QuoteHasNoUsablePrice_Warns()
        {
            // A quote that arrives but carries neither a last price nor a bid/ask is just as stale
            // in effect, and was equally silent.
            _quotes.Setup(x => x.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Quote(0m));

            await Create().FetchLatestAsync(Symbol);

            AssertLogged(LogLevel.Warning);
        }

        [Fact]
        public async Task QuoteThrows_WarnsRatherThanDebug()
        {
            // Was LogDebug; production's minimum level is Information, so it never surfaced.
            _quotes.Setup(x => x.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("boom"));

            var result = await Create().FetchLatestAsync(Symbol);

            Assert.Equal(109.15m, result.HistoricalPrices[^1].Close);
            AssertLogged(LogLevel.Warning);
        }

        [Fact]
        public async Task QuoteUsable_OverlaysTodayAndSaysNothing()
        {
            // Guard against turning a working path into log noise on every request.
            _quotes.Setup(x => x.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Quote(107.7m));

            var result = await Create().FetchLatestAsync(Symbol);

            Assert.Equal(107.7m, result.HistoricalPrices[^1].Close);
            AssertLogged(LogLevel.Warning, Times.Never());
        }

        [Fact]
        public async Task OutsideTheQuoteWindow_SaysNothing()
        {
            // Before 09:00 there is no same-day quote to expect, so silence is correct — the
            // warning must mean "today's price is missing when it should exist".
            _quotes.Setup(x => x.GetLatestQuoteAsync(Symbol, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockQuote?)null);

            var beforeOpen = new DateTimeOffset(2026, 9, 11, 0, 30, 0, TimeSpan.Zero); // 08:30 Taipei
            await Create(beforeOpen).FetchLatestAsync(Symbol);

            AssertLogged(LogLevel.Warning, Times.Never());
            _quotes.Verify(
                x => x.GetLatestQuoteAsync(It.IsAny<string>(), It.IsAny<MarketType?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>StockQuote inherits required members from StockData; fill them once here.</summary>
        private static StockQuote Quote(decimal lastPrice) => new()
        {
            Symbol = Symbol,
            Name = "元大台灣50",
            Market = MarketType.TSE,
            LastPrice = lastPrice,
        };

        private static MarketData HistoricalOnly() => new()
        {
            StockCode = Symbol,
            HistoricalPrices =
            [
                new() { Date = new DateTime(2026, 9, 10), Open = 109m, High = 110m, Low = 108m, Close = 109.15m, Volume = 1000 },
            ],
        };

        private TwTodayOverlayMarketDataProvider Create(DateTimeOffset? now = null) =>
            new(_inner.Object, _quotes.Object, _logger.Object, new FakeTimeProvider(now ?? FridayAfternoon));

        private void AssertLogged(LogLevel level, Times? times = null) =>
            _logger.Verify(
                x => x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                times ?? Times.Once());

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
