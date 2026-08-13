using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// GetBenchmarkComparisonAsync simulates "what if every contribution had bought each requested
    /// benchmark instead" — driven by the exact same cost-basis-delta contribution proxy
    /// ComputeAnnualizedReturn (XIRR) already uses, applied independently per benchmark symbol
    /// (the contribution amount is shared; only each symbol's own buy price differs). These tests
    /// pin the unit-buying/selling math, the cost-tracked-only universe, and multi-symbol
    /// independence. Anchor date and "today" are both pinned via an injected TimeProvider rather
    /// than derived from the real clock — see PortfolioDrawdownTests for why a relative-to-real-now
    /// anchor drifts out of the service's own lookback window over time.
    /// </summary>
    public class PortfolioBenchmarkComparisonTests : IDisposable
    {
        private const string TestUserId = "benchmark-test-user";

        // Monday, fixed — not derived from the real clock (see class docs).
        private static readonly DateOnly AnchorMonday = new(2026, 7, 20);
        // Comfortably inside the 30-day window every test here queries with, regardless of the real date.
        private static readonly DateOnly FixedToday = AnchorMonday.AddDays(20);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;
        private readonly PortfolioService _service;

        public PortfolioBenchmarkComparisonTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var creditService = new Mock<ICreditService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                stockService: null!,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                creditService.Object,
                NullLogger<PortfolioService>.Instance,
                new FakeTimeProvider(new DateTimeOffset(FixedToday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetBenchmarkComparison_SingleContribution_SimulatesBuyingBenchmarkAtThatDaysPrice()
        {
            var snapshotDate = Weekday(0);
            // Cost basis 90,000 booked on day 0 — the contribution-flow proxy sees this as a single
            // 90,000 inflow on day 0, same as ComputeAnnualizedReturn would.
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (Weekday(0), 1000m), (Weekday(1), 1100m));
            // Benchmark (0050) at 100/unit on day 0 -> 90,000 / 100 = 900 simulated units.
            SetupStockProvider("0050", MarketRegion.TW, (Weekday(0), 100m), (Weekday(1), 110m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            // Only two real price bars given — both prices forward-fill from Weekday(1) through
            // "today", so the window has more than 2 dates; the values matter, not the count.
            var benchmark = Assert.Single(result.Benchmarks);
            Assert.Equal("0050", benchmark.Symbol);
            Assert.NotEmpty(result.Dates);

            var day0Idx = result.Dates.IndexOf(Weekday(0).ToString("yyyy-MM-dd"));
            Assert.Equal(100_000m, result.PortfolioTWD[day0Idx]); // 100 shares * 1000
            Assert.Equal(90_000m, benchmark.ValuesTWD[day0Idx]);  // 900 units * 100

            var day1Idx = result.Dates.IndexOf(Weekday(1).ToString("yyyy-MM-dd"));
            Assert.Equal(110_000m, result.PortfolioTWD[day1Idx]); // 100 shares * 1100
            Assert.Equal(99_000m, benchmark.ValuesTWD[day1Idx]);  // 900 units * 110 (no new contribution)

            // Period return (last date in the window vs. first) — the last date mirrors day1's
            // values since both prices are flat after it: (110,000-100,000)/100,000 = 10%;
            // (99,000-90,000)/90,000 = 10%.
            Assert.Equal(10m, result.PortfolioReturnPct);
            Assert.Equal(10m, benchmark.ReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_SecondContribution_BuysMoreUnitsAtNewPrice()
        {
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 50_000,
                });
            // Second snapshot adds another 50,000 of cost basis (delta = +50,000 on day1).
            await SeedSnapshotAsync(day1,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 200, CostBasis = 100_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (day0, 500m), (day1, 500m));
            // Day0: 50,000 / 100 = 500 units. Day1 price rises to 200: buy 50,000 / 200 = 250 more units.
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 200m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            var day1Idx = result.Dates.IndexOf(day1.ToString("yyyy-MM-dd"));
            // (500 + 250) units * 200 = 150,000.
            Assert.Equal(150_000m, result.Benchmarks.Single().ValuesTWD[day1Idx]);
        }

        [Fact]
        public async Task GetBenchmarkComparison_ContributionFromBeforeTheWindow_SeedsStartingUnitsInsteadOfBuyingAllAtTheLaterPrice()
        {
            // Regression test: a snapshot dated well before the query window's startDate used to
            // be invisible to the day loop (it only reacts to `date == active.EffectiveDate`,
            // which never fires for a pre-window date) — so the cost basis it represented got
            // silently folded into whatever the NEXT in-window contribution's delta was,
            // buying the entire historical cost basis at that later date's price instead of the
            // price that was actually in effect when the window opens.
            var oldDate = AnchorMonday.AddDays(-60); // well before startDate (AnchorMonday - 10 for days: 30)
            var day0 = Weekday(0); // = AnchorMonday, inside the window
            await SeedSnapshotAsync(oldDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 500, CostBasis = 500_000,
                });
            // In-window top-up: cost basis rises from 500,000 to 600,000 — only this 100,000
            // delta should be bought at day0's benchmark price.
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 600, CostBasis = 600_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (oldDate, 1000m), (day0, 1000m));
            // 0050 was 50/unit back when the pre-window cost basis was contributed, risen to
            // 100/unit by day0 — the two prices must differ for this test to actually distinguish
            // "seeded at window-start price" from the bug's "bought at day0's price regardless".
            SetupStockProvider("0050", MarketRegion.TW, (oldDate, 50m), (day0, 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            var day0Idx = result.Dates.IndexOf(day0.ToString("yyyy-MM-dd"));
            // Correct: 500,000 seeded at the window-start price (50) = 10,000 units, plus the
            // 100,000 in-window delta bought at day0's price (100) = 1,000 more units.
            // (10,000 + 1,000) * 100 = 1,100,000.
            // The bug would instead show 600,000 (the full cumulative cost bought once at day0's
            // price: 600,000 / 100 = 6,000 units * 100 = 600,000).
            Assert.Equal(1_100_000m, result.Benchmarks.Single().ValuesTWD[day0Idx]);
        }

        [Fact]
        public async Task GetBenchmarkComparison_PositionWithoutCostBasis_ExcludedFromBothSides()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    // No CostBasis — same "untracked cost" case the existing PnL/XIRR logic already
                    // excludes; must not appear on the portfolio side, and must not drive a
                    // benchmark contribution either.
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = null,
                });
            SetupStockProvider("2330", MarketRegion.TW, (Weekday(0), 1000m));
            SetupStockProvider("0050", MarketRegion.TW, (Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            // Forward-fill produces many dates from Weekday(0) through "today", all flat at 0 since
            // the untracked-cost position never contributes to either side.
            Assert.NotEmpty(result.Dates);
            Assert.All(result.PortfolioTWD, v => Assert.Equal(0m, v));
            Assert.All(result.Benchmarks.Single().ValuesTWD, v => Assert.Equal(0m, v));
        }

        [Fact]
        public async Task GetBenchmarkComparison_UnknownBenchmarkSymbol_ReturnsEmptyResult()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (Weekday(0), 1000m));
            // No provider set up for "0050" at all — DetectRegion/GetMarketDataProvider return
            // Moq defaults, so FetchStocksAsync's per-symbol fetch fails soft for it.

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.Empty(result.Dates);
            Assert.Null(result.PortfolioReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_MultipleSymbols_EachSimulatedIndependently()
        {
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (day0, 1000m), (day1, 1000m));
            // 90,000 / 100 = 900 units of 0050; 90,000 / 300 = 300 units of QQQ — independent buys.
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 200m));
            SetupStockProvider("QQQ", MarketRegion.US, (day0, 300m), (day1, 300m));

            var result = await _service.GetBenchmarkComparisonAsync(
                TestUserId, days: 30, benchmarkSymbols: ["0050", "QQQ"]);

            Assert.Equal(2, result.Benchmarks.Count);
            var day1Idx = result.Dates.IndexOf(day1.ToString("yyyy-MM-dd"));

            var fifty = result.Benchmarks.Single(b => b.Symbol == "0050");
            Assert.Equal(180_000m, fifty.ValuesTWD[day1Idx]); // 900 * 200 — price doubled

            var qqq = result.Benchmarks.Single(b => b.Symbol == "QQQ");
            Assert.Equal(90_000m, qqq.ValuesTWD[day1Idx]); // 300 * 300 — price flat, matches portfolio's own return
        }

        [Fact]
        public async Task GetBenchmarkComparison_OneOfMultipleSymbolsUnpriceable_DropsItButKeepsTheOthers()
        {
            var snapshotDate = Weekday(0);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (Weekday(0), 1000m));
            SetupStockProvider("0050", MarketRegion.TW, (Weekday(0), 100m));
            // "NOPE" deliberately has no provider setup at all — should be silently dropped.

            var result = await _service.GetBenchmarkComparisonAsync(
                TestUserId, days: 30, benchmarkSymbols: ["0050", "NOPE"]);

            var benchmark = Assert.Single(result.Benchmarks);
            Assert.Equal("0050", benchmark.Symbol);
        }

        [Fact]
        public async Task GetBenchmarkComparison_NoSnapshots_ReturnsEmptyResultWithRequestedSymbols()
        {
            var result = await _service.GetBenchmarkComparisonAsync(
                TestUserId, days: 30, benchmarkSymbols: ["0050", "VT"]);

            Assert.Empty(result.Dates);
            Assert.Equal(["0050", "VT"], result.Benchmarks.Select(b => b.Symbol));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static DateOnly Weekday(int businessDaysOffset)
        {
            var d = AnchorMonday;
            var remaining = businessDaysOffset;
            while (remaining > 0)
            {
                d = d.AddDays(1);
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) remaining--;
            }
            return d;
        }

        private async Task SeedSnapshotAsync(DateOnly effectiveDate, params HoldingPosition[] positions)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = effectiveDate,
                UsdTwdRate = 32m,
                Positions = positions.ToList(),
            });
            await _dbContext.SaveChangesAsync();
        }

        // Shared per-region provider mock: GetMarketDataProvider is keyed by MarketRegion, not by
        // symbol, so two symbols in the same region (portfolio holding + benchmark, both TW) must
        // land on the SAME provider mock with one FetchAsync setup per symbol — otherwise the
        // second SetupStockProvider call for that region silently clobbers the first via Moq's
        // last-setup-wins semantics, and the first symbol's FetchAsync call falls through to an
        // unconfigured (null-returning) mock.
        private readonly Dictionary<MarketRegion, Mock<IMarketDataProvider>> _regionProviders = new();

        private void SetupStockProvider(string symbol, MarketRegion region, params (DateOnly Date, decimal Close)[] prices)
        {
            _providerFactory.Setup(x => x.DetectRegion(symbol)).Returns(region);

            if (!_regionProviders.TryGetValue(region, out var provider))
            {
                provider = new Mock<IMarketDataProvider>();
                _regionProviders[region] = provider;
                _providerFactory.Setup(x => x.GetMarketDataProvider(region)).Returns(provider.Object);
            }

            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = symbol,
                    HistoricalPrices = prices
                        .Select(p => new DailyPrice { Date = p.Date.ToDateTime(TimeOnly.MinValue), Close = p.Close })
                        .ToList(),
                });
        }
    }
}
