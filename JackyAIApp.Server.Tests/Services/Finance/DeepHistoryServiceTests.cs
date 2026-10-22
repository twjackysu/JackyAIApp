using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// DeepHistoryService adds what the backfill has stored to the ~6 months the providers hand out.
    /// It only reads: nothing it does may make a request wait on an exchange.
    /// </summary>
    public class DeepHistoryServiceTests : IDisposable
    {
        private static readonly DateOnly Target = new(2026, 1, 1);

        private readonly DbContextOptions<AzureSQLDBContext> _options;
        private readonly AzureSQLDBContext _db;
        private readonly Mock<ITwHistoryBackfill> _backfill = new();
        private readonly Mock<IMarketDataProviderFactory> _providers = new();
        private readonly DeepHistoryService _service;
        private IReadOnlyList<HistoryWant>? _requested;

        public DeepHistoryServiceTests()
        {
            _options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(_options);

            _providers.Setup(p => p.DetectRegion(It.IsAny<string>()))
                .Returns((string code) => char.IsDigit(code[0]) ? MarketRegion.TW : MarketRegion.US);
            _backfill
                .Setup(b => b.RequestAsync(It.IsAny<IReadOnlyList<HistoryWant>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyList<HistoryWant> wants, DateOnly _, CancellationToken _) => _requested = wants)
                .ReturnsAsync(new BackfillStatus(BackfillState.Running, new DateOnly(2026, 4, 1), Target));

            _service = new DeepHistoryService(_backfill.Object, new TestDbContextFactory(_options), _providers.Object);
        }

        public void Dispose() => _db.Dispose();

        private static DailyPrice Bar(DateOnly day, decimal close = 100m, long volume = 5) => new()
        {
            Date = day.ToDateTime(TimeOnly.MinValue), Open = close, High = close, Low = close, Close = close, Volume = volume,
        };

        private static MarketData Recent(string symbol, params DailyPrice[] bars) =>
            new() { StockCode = symbol, CompanyName = "name", HistoricalPrices = [.. bars] };

        private async Task StoreAsync(string symbol, DateOnly day, decimal close, long volume = 1000)
        {
            _db.DailyStockPrices.Add(new DailyStockPrice
            {
                Symbol = symbol, Market = "TW", TradeDate = day, Open = close, High = close, Low = close, Close = close, Volume = volume,
            });
            await _db.SaveChangesAsync();
        }

        private async Task InfoAsync(string symbol, string market = "TSE")
        {
            _db.StockSymbolInfos.Add(new StockSymbolInfo { Symbol = symbol, Market = "TW", TwMarketType = market });
            await _db.SaveChangesAsync();
        }

        [Fact]
        public async Task StoredBarsAreAddedInFrontOfTheRecentOnes_InDateOrder()
        {
            await InfoAsync("2330");
            await StoreAsync("2330", new DateOnly(2026, 2, 3), 90);
            await StoreAsync("2330", new DateOnly(2026, 2, 2), 80);
            var recent = Recent("2330", Bar(new DateOnly(2026, 5, 4)), Bar(new DateOnly(2026, 5, 5)));

            var result = await _service.ExtendAsync(
                new Dictionary<string, MarketData> { ["2330"] = recent }, [new HistoryWant("2330", Target, null)], Target, CancellationToken.None);

            Assert.Equal(
                [new DateTime(2026, 2, 2), new DateTime(2026, 2, 3), new DateTime(2026, 5, 4), new DateTime(2026, 5, 5)],
                result.Stocks["2330"].HistoricalPrices.Select(p => p.Date));
            Assert.Equal(Running(), result.Backfill);
        }

        private static BackfillStatus Running() => new(BackfillState.Running, new DateOnly(2026, 4, 1), Target);

        [Fact]
        public async Task TheInputIsNotChanged_ItIsTheCopyEveryOtherRequestShares()
        {
            await InfoAsync("2330");
            await StoreAsync("2330", new DateOnly(2026, 2, 2), 80);
            var recent = Recent("2330", Bar(new DateOnly(2026, 5, 4)));
            var input = new Dictionary<string, MarketData> { ["2330"] = recent };

            var result = await _service.ExtendAsync(input, [new HistoryWant("2330", Target, null)], Target, CancellationToken.None);

            Assert.Single(recent.HistoricalPrices);
            Assert.NotSame(recent, result.Stocks["2330"]);
            Assert.Same(recent, input["2330"]);
        }

        [Fact]
        public async Task StoredBarsFromTheDayTheRecentOnesStartOnward_AreLeftToTheRecentOnes()
        {
            // The providers' window carries today's overlay and is the fresher word on every day it covers.
            await InfoAsync("2330");
            await StoreAsync("2330", new DateOnly(2026, 5, 4), 1);
            await StoreAsync("2330", new DateOnly(2026, 5, 5), 1);
            var recent = Recent("2330", Bar(new DateOnly(2026, 5, 4), 100), Bar(new DateOnly(2026, 5, 5), 101));

            var result = await _service.ExtendAsync(
                new Dictionary<string, MarketData> { ["2330"] = recent }, [new HistoryWant("2330", Target, null)], Target, CancellationToken.None);

            Assert.Equal([100m, 101m], result.Stocks["2330"].HistoricalPrices.Select(p => p.Close));
        }

        [Fact]
        public async Task OnlyBarsFromTheMonthTheStockIsNeededFromAreAdded()
        {
            await InfoAsync("2330");
            await StoreAsync("2330", new DateOnly(2025, 12, 31), 50);   // before the month it is needed from
            await StoreAsync("2330", new DateOnly(2026, 1, 2), 60);
            var recent = Recent("2330", Bar(new DateOnly(2026, 5, 4)));

            var result = await _service.ExtendAsync(
                new Dictionary<string, MarketData> { ["2330"] = recent },
                [new HistoryWant("2330", new DateOnly(2026, 1, 20), null)], Target, CancellationToken.None);

            // From the start of January: the whole month has been fetched, and CloseOnOrBefore for
            // the 20th wants the close of the 2nd.
            Assert.Equal([new DateTime(2026, 1, 2), new DateTime(2026, 5, 4)], result.Stocks["2330"].HistoricalPrices.Select(p => p.Date));
        }

        [Fact]
        public async Task AnOtcStocksStoredVolume_IsHandedOutInShares()
        {
            // Stored as TPEx delivers it (lots), like the recent bars before they are scaled.
            await InfoAsync("6488", "OTC");
            await StoreAsync("6488", new DateOnly(2026, 2, 2), 80, volume: 7);
            var recent = Recent("6488", Bar(new DateOnly(2026, 5, 4)));

            var result = await _service.ExtendAsync(
                new Dictionary<string, MarketData> { ["6488"] = recent }, [new HistoryWant("6488", Target, null)], Target, CancellationToken.None);

            Assert.Equal(7000, result.Stocks["6488"].HistoricalPrices[0].Volume);
        }

        [Fact]
        public async Task UsStocksAndStocksWithoutRecentPrices_AreNotAskedAbout()
        {
            await InfoAsync("2330");
            var stocks = new Dictionary<string, MarketData>
            {
                ["2330"] = Recent("2330", Bar(new DateOnly(2026, 5, 4))),
                ["AAPL"] = Recent("AAPL", Bar(new DateOnly(2026, 5, 4))),
            };

            await _service.ExtendAsync(
                stocks,
                [new HistoryWant("2330", Target, null), new HistoryWant("AAPL", Target, null), new HistoryWant("2317", Target, null)],
                Target, CancellationToken.None);

            Assert.Equal(["2330"], _requested!.Select(w => w.Symbol));
        }

        [Fact]
        public async Task WithNothingTWToExtend_NoBackfillIsReportedAndTheBackfillIsNotAsked()
        {
            var stocks = new Dictionary<string, MarketData> { ["AAPL"] = Recent("AAPL", Bar(new DateOnly(2026, 5, 4))) };

            var result = await _service.ExtendAsync(stocks, [new HistoryWant("AAPL", Target, null)], Target, CancellationToken.None);

            Assert.Null(result.Backfill);
            Assert.Same(stocks["AAPL"], result.Stocks["AAPL"]);
            _backfill.Verify(b => b.RequestAsync(It.IsAny<IReadOnlyList<HistoryWant>>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AStockWithNothingStoredYet_IsReturnedAsItWas()
        {
            await InfoAsync("2330");
            var recent = Recent("2330", Bar(new DateOnly(2026, 5, 4)));

            var result = await _service.ExtendAsync(
                new Dictionary<string, MarketData> { ["2330"] = recent }, [new HistoryWant("2330", Target, null)], Target, CancellationToken.None);

            Assert.Same(recent, result.Stocks["2330"]);
            Assert.NotNull(result.Backfill);
        }
    }
}
