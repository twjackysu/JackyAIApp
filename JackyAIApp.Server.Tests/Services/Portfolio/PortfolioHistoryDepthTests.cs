using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// The drawdown and benchmark charts reach back further than the ~6 months of prices the
    /// providers hand out by adding what the background backfill has stored (IDeepHistoryService).
    /// These pin what is asked of it, that a window the providers already cover never touches it,
    /// and that a failure in it costs the chart nothing but the extra history.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioHistoryDepthTests : IDisposable
    {
        private const string TestUserId = "depth-test-user";

        // Monday. Far enough before "today" that the window runs past the providers' 6 months.
        private static readonly DateOnly FirstHeld = new(2026, 1, 5);
        private static readonly DateOnly FixedToday = new(2026, 8, 10);
        private static readonly DateOnly RecentBar = new(2026, 7, 20);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly FakeDeepHistory _deepHistory = new();
        private readonly DrawdownAnalysisService _drawdown;
        private readonly BenchmarkComparisonService _benchmark;

        public PortfolioHistoryDepthTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var builder = new PortfolioServiceBuilder(_dbContext)
            {
                Providers = _providerFactory.Object,
                Dividends = dividendData.Object,
                Time = new FakeTimeProvider(new DateTimeOffset(FixedToday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)),
                DeepHistory = _deepHistory,
            };
            _drawdown = builder.BuildDrawdown();
            _benchmark = builder.BuildBenchmark();
        }

        public void Dispose() => _dbContext.Dispose();

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private sealed class FakeDeepHistory : IDeepHistoryService
        {
            public List<(IReadOnlyList<HistoryWant> Wants, DateOnly Target)> Calls { get; } = [];
            public Func<IReadOnlyDictionary<string, MarketData>, DeepHistoryResult>? Respond { get; set; }
            public Exception? Throw { get; set; }

            public Task<DeepHistoryResult> ExtendAsync(
                IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyList<HistoryWant> wants,
                DateOnly targetFrom, CancellationToken ct)
            {
                Calls.Add((wants, targetFrom));
                if (Throw is not null) throw Throw;
                return Task.FromResult(Respond?.Invoke(stocks) ?? new DeepHistoryResult(new Dictionary<string, MarketData>(stocks), null));
            }
        }

        private async Task SeedSnapshotAsync(DateOnly effectiveDate, params HoldingPosition[] positions)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId, EffectiveDate = effectiveDate, UsdTwdRate = 32m, Positions = positions.ToList(),
            });
            await _dbContext.SaveChangesAsync();
        }

        private static HoldingPosition Tsmc(decimal? costBasis = null) => new()
        {
            AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電", Market = "TW",
            Currency = "TWD", Quantity = 100, CostBasis = costBasis,
        };

        private void ProviderReturns(string symbol, params (DateOnly Date, decimal Close)[] prices)
        {
            _providerFactory.Setup(x => x.DetectRegion(symbol)).Returns(MarketRegion.TW);
            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Data(symbol, prices));
            _providerFactory.Setup(x => x.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }

        private static MarketData Data(string symbol, params (DateOnly Date, decimal Close)[] prices) => new()
        {
            StockCode = symbol,
            HistoricalPrices = prices.Select(p => new DailyPrice { Date = p.Date.ToDateTime(TimeOnly.MinValue), Close = p.Close }).ToList(),
        };

        private static BackfillStatus Running(DateOnly coveredFrom, DateOnly target) =>
            new(BackfillState.Running, coveredFrom, target);

        [Fact]
        public async Task TheDrawdownChart_ReachesBackAsFarAsTheStoredPricesDo()
        {
            await SeedSnapshotAsync(FirstHeld, Tsmc());
            ProviderReturns("2330", (RecentBar, 1000m));
            _deepHistory.Respond = stocks => new DeepHistoryResult(
                new Dictionary<string, MarketData>
                {
                    ["2330"] = Data("2330", (FirstHeld, 900m), (RecentBar, 1000m)),
                },
                Running(new DateOnly(2026, 1, 1), FirstHeld));

            var result = await _drawdown.GetDrawdownAnalysisAsync(TestUserId, days: 365);

            // Without the stored bar there is no price before 07-20, so nothing before it is drawn.
            Assert.Equal("2026-01-05", result.Points[0].Date);
            Assert.NotNull(result.Backfill);
            Assert.Equal(BackfillState.Running, result.Backfill.State);
            Assert.Equal("2026-01-01", result.Backfill.CoveredFrom);
            Assert.Equal("2026-01-05", result.Backfill.TargetFrom);
        }

        [Fact]
        public async Task TheDrawdownChart_AsksForTheHistoryTheHeldStocksNeed()
        {
            await SeedSnapshotAsync(FirstHeld, Tsmc());
            await SeedSnapshotAsync(new DateOnly(2026, 4, 6), Tsmc(), new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "0050", Name = "元大台灣50", Market = "TW", Currency = "TWD", Quantity = 10,
            });
            ProviderReturns("2330", (RecentBar, 1000m));
            _providerFactory.Setup(x => x.DetectRegion("0050")).Returns(MarketRegion.TW);

            await _drawdown.GetDrawdownAnalysisAsync(TestUserId, days: 365);

            var call = Assert.Single(_deepHistory.Calls);
            Assert.Equal(FirstHeld, call.Target);
            var wants = call.Wants.ToDictionary(w => w.Symbol);
            Assert.Equal(new HistoryWant("2330", FirstHeld, FirstHeld), wants["2330"]);
            // Held from April: nothing before late March is needed, however long the chart.
            Assert.Equal(new HistoryWant("0050", new DateOnly(2026, 3, 27), new DateOnly(2026, 4, 6)), wants["0050"]);
        }

        [Fact]
        public async Task AWindowTheProvidersAlreadyCover_NeverTouchesTheDeepHistory()
        {
            var recent = new DateOnly(2026, 7, 20);
            await SeedSnapshotAsync(recent, Tsmc());
            ProviderReturns("2330", (recent, 1000m));

            var result = await _drawdown.GetDrawdownAnalysisAsync(TestUserId, days: 90);

            Assert.Empty(_deepHistory.Calls);
            Assert.Null(result.Backfill);
            Assert.NotEmpty(result.Points);
        }

        [Fact]
        public async Task IfTheDeepHistoryFails_TheChartIsDrawnFromThePricesInHand()
        {
            await SeedSnapshotAsync(FirstHeld, Tsmc());
            ProviderReturns("2330", (RecentBar, 1000m));
            _deepHistory.Throw = new InvalidOperationException("db down");

            var result = await _drawdown.GetDrawdownAnalysisAsync(TestUserId, days: 365);

            Assert.Equal("2026-07-20", result.Points[0].Date);
            Assert.Null(result.Backfill);
        }

        [Fact]
        public async Task TheBackfillIsReportedEvenWhileThereIsNothingToDrawYet()
        {
            // Every price is still to come: an empty chart with a progress bar, not an empty chart alone.
            await SeedSnapshotAsync(FirstHeld, Tsmc());
            ProviderReturns("2330");
            _deepHistory.Respond = stocks => new DeepHistoryResult(
                new Dictionary<string, MarketData>(stocks), Running(new DateOnly(2026, 5, 1), FirstHeld));

            var result = await _drawdown.GetDrawdownAnalysisAsync(TestUserId, days: 365);

            Assert.Empty(result.Points);
            Assert.Equal(BackfillState.Running, result.Backfill?.State);
        }

        [Fact]
        public async Task TheBenchmarkChart_AsksForTheBenchmarkOverTheWholeWindow_AndReportsTheBackfill()
        {
            await SeedSnapshotAsync(FirstHeld, Tsmc(costBasis: 90_000m));
            ProviderReturns("2330", (RecentBar, 1000m));
            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(x => x.FetchAsync("0050", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Data("0050", (RecentBar, 150m)));
            provider.Setup(x => x.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Data("2330", (RecentBar, 1000m)));
            _providerFactory.Setup(x => x.DetectRegion("0050")).Returns(MarketRegion.TW);
            _providerFactory.Setup(x => x.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
            _deepHistory.Respond = stocks => new DeepHistoryResult(
                new Dictionary<string, MarketData>(stocks), Running(new DateOnly(2026, 5, 1), FirstHeld));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 365, ["0050"]);

            var wants = Assert.Single(_deepHistory.Calls).Wants.ToDictionary(w => w.Symbol);
            Assert.Equal(new HistoryWant("0050", FirstHeld, null), wants["0050"]);
            Assert.Equal(BackfillState.Running, result.Backfill?.State);
            Assert.Equal("2026-05-01", result.Backfill?.CoveredFrom);
        }

        // ── HistoryWants ──────────────────────────────────────────────────────

        private static HoldingSnapshot Snapshot(DateOnly date, params HoldingPosition[] positions) =>
            new() { UserId = "u", EffectiveDate = date, UsdTwdRate = 32m, Positions = positions.ToList() };

        private static HoldingPosition Stock(string symbol) => new()
        {
            AssetType = AssetType.STOCK, Symbol = symbol, Name = symbol, Market = "TW", Currency = "TWD", Quantity = 1,
        };

        [Fact]
        public void AHeldStockIsNeededFromABitBeforeItWasFirstHeld_NotFromTheStartOfTheWindow()
        {
            var start = new DateOnly(2023, 1, 1);
            var wants = PortfolioHistoryExtender.HistoryWants(
                [Snapshot(new DateOnly(2025, 6, 2), Stock("2330")), Snapshot(new DateOnly(2025, 9, 1), Stock("2330"), Stock("0050"))],
                [], start);

            var byCode = wants.ToDictionary(w => w.Symbol);
            Assert.Equal(new HistoryWant("2330", new DateOnly(2025, 5, 23), new DateOnly(2025, 6, 2)), byCode["2330"]);
            Assert.Equal(new HistoryWant("0050", new DateOnly(2025, 8, 22), new DateOnly(2025, 9, 1)), byCode["0050"]);
        }

        [Fact]
        public void AStockHeldBeforeTheWindow_IsNeededFromTheStartOfTheWindow()
        {
            var start = new DateOnly(2025, 1, 1);
            var wants = PortfolioHistoryExtender.HistoryWants([Snapshot(new DateOnly(2020, 3, 2), Stock("2330"))], [], start);

            Assert.Equal(new HistoryWant("2330", start, new DateOnly(2020, 3, 2)), Assert.Single(wants));
        }

        [Fact]
        public void ABenchmarkIsNeededOverTheWholeWindow_EvenIfItIsAlsoHeldLater()
        {
            var start = new DateOnly(2023, 1, 1);
            var wants = PortfolioHistoryExtender.HistoryWants([Snapshot(new DateOnly(2025, 6, 2), Stock("0050"))], ["0050", "0056"], start);

            var byCode = wants.ToDictionary(w => w.Symbol);
            Assert.Equal(new HistoryWant("0050", start, new DateOnly(2025, 6, 2)), byCode["0050"]);
            Assert.Equal(new HistoryWant("0056", start, null), byCode["0056"]);
        }

        [Fact]
        public void OnlyStocksAreWanted_AndOnlyAFewBenchmarks()
        {
            var cash = new HoldingPosition { AssetType = AssetType.CASH, Symbol = "", Name = "cash", Market = "TW", Currency = "TWD", Quantity = 1 };
            var fund = new HoldingPosition { AssetType = AssetType.FUND, Symbol = "ALI006", Name = "fund", Market = "FUND", Currency = "TWD", Quantity = 1 };
            string[] benchmarks = ["B1", "B2", "B3", "B4", "B5", "B6", "B7"];

            var wants = PortfolioHistoryExtender.HistoryWants(
                [Snapshot(new DateOnly(2025, 6, 2), Stock("2330"), cash, fund)], benchmarks, new DateOnly(2023, 1, 1));

            Assert.Equal(
                new[] { "2330", "B1", "B2", "B3", "B4", "B5" },
                wants.Select(w => w.Symbol));
        }
    }
}
