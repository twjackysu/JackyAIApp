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
    /// GetBenchmarkComparisonAsync simulates "what if every contribution had bought each requested
    /// benchmark instead" — driven by the exact same cost-basis-delta contribution proxy
    /// ComputeAnnualizedReturn (XIRR) already uses, applied independently per benchmark symbol
    /// (the contribution amount is shared; only each symbol's own buy price differs). These tests
    /// pin the unit-buying/selling math, the cost-tracked-only universe, and multi-symbol
    /// independence. Anchor date and "today" are both pinned via an injected TimeProvider rather
    /// than derived from the real clock — see PortfolioDrawdownTests for why a relative-to-real-now
    /// anchor drifts out of the service's own lookback window over time.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioBenchmarkComparisonTests : IDisposable
    {
        private const string TestUserId = "benchmark-test-user";

        // Monday, fixed — not derived from the real clock (see class docs).
        private static readonly DateOnly AnchorMonday = new(2026, 7, 20);
        // Comfortably inside the 30-day window every test here queries with, regardless of the real date.
        private static readonly DateOnly FixedToday = AnchorMonday.AddDays(20);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;
        private readonly Mock<IDividendService> _dividendData;
        private readonly BenchmarkComparisonService _benchmark;

        public PortfolioBenchmarkComparisonTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            _providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            _dividendData = new Mock<IDividendService>();
            _dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            _dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var categoryService = new Mock<IStockCategoryService>();

            var builder = new PortfolioServiceBuilder(_dbContext)
            {
                Providers = _providerFactory.Object,
                FundData = fundData.Object,
                Dividends = _dividendData.Object,
                Categories = categoryService.Object,
                HttpClientFactory = httpClientFactory.Object,
                Cache = cache,
                Time = new FakeTimeProvider(new DateTimeOffset(FixedToday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)),
            };
            _benchmark = builder.BuildBenchmark();
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

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

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
        public async Task GetBenchmarkComparison_ACostInUsd_IsContributedAtTheSnapshotsRate()
        {
            // US$1,000 of cost at the snapshot's 32 is a NT$32,000 contribution: 320 units of 0050 at 100.
            await SeedSnapshotAsync(Weekday(0),
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "SPY", Name = "SPY",
                    Market = "US", Currency = "USD", CostCurrency = "USD", Quantity = 2, CostBasis = 1_000m,
                });
            SetupStockProvider("SPY", MarketRegion.US, (Weekday(0), 500m));
            SetupStockProvider("0050", MarketRegion.TW, (Weekday(0), 100m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            var day0 = result.Dates.IndexOf(Weekday(0).ToString("yyyy-MM-dd"));
            Assert.Equal(32_000m, result.PortfolioTWD[day0]);
            Assert.Equal(32_000m, Assert.Single(result.Benchmarks).ValuesTWD[day0]);
        }

        // ── A contribution on a day that is not recorded ────────────────────────────────────────

        private static HoldingPosition Tsmc(int shares) => new()
        {
            AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
            Market = "TW", Currency = "TWD", Quantity = shares, CostBasis = shares * 1000m,
        };

        private static (DateOnly, decimal)[] Flat(decimal close, int fromWeekday = 0) =>
            [.. Enumerable.Range(fromWeekday, 15).Select(i => (Weekday(i), close))];

        [Fact]
        public async Task GetBenchmarkComparison_ASnapshotDatedOnAWeekend_IsAContributionOnTheNextTradingDay_NotAGain()
        {
            // Prices never move; the holding doubles in a snapshot saved on Saturday.
            var saturday = AnchorMonday.AddDays(5);
            await SeedSnapshotAsync(Weekday(0), Tsmc(10));
            await SeedSnapshotAsync(saturday, Tsmc(20));
            SetupStockProvider("2330", MarketRegion.TW, Flat(1000m));
            SetupStockProvider("0050", MarketRegion.TW, Flat(100m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.Equal(0m, result.PortfolioReturnPct);
            var benchmark = Assert.Single(result.Benchmarks);
            Assert.Equal(0m, benchmark.ReturnPct);
            // Monday: the 10,000 more bought 100 more units.
            Assert.Equal(20_000m, benchmark.ValuesTWD[result.Dates.IndexOf(saturday.AddDays(2).ToString("yyyy-MM-dd"))]);
        }

        [Fact]
        public async Task GetBenchmarkComparison_AContributionOnADayThePortfolioCannotBeValued_IsNotAGain()
        {
            // The snapshot adds 2317, whose prices start the next day: the portfolio has no value on the
            // snapshot's own day, so the contribution counts on the next day that is recorded.
            await SeedSnapshotAsync(Weekday(0), Tsmc(10));
            await SeedSnapshotAsync(Weekday(3), Tsmc(10), new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2317", Name = "鴻海",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 20_000m,
            });
            SetupStockProvider("2330", MarketRegion.TW, Flat(1000m));
            SetupStockProvider("2317", MarketRegion.TW, Flat(200m, fromWeekday: 4));
            SetupStockProvider("0050", MarketRegion.TW, Flat(100m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.DoesNotContain(Weekday(3).ToString("yyyy-MM-dd"), result.Dates);
            Assert.Equal(0m, result.PortfolioReturnPct);
            Assert.Equal(0m, Assert.Single(result.Benchmarks).ReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_ABenchmarkWithNoPriceYet_BuysAtItsFirstPrice()
        {
            await SeedSnapshotAsync(Weekday(0), Tsmc(10));
            SetupStockProvider("2330", MarketRegion.TW, Flat(1000m));
            SetupStockProvider("0050", MarketRegion.TW, Flat(100m, fromWeekday: 2));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            var benchmark = Assert.Single(result.Benchmarks);
            Assert.Equal(10_000m, benchmark.ValuesTWD[result.Dates.IndexOf(Weekday(2).ToString("yyyy-MM-dd"))]);
            Assert.Equal(0m, benchmark.ReturnPct);
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

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

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

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            var day0Idx = result.Dates.IndexOf(day0.ToString("yyyy-MM-dd"));
            // Correct: 500,000 seeded at the window-start price (50) = 10,000 units, plus the
            // 100,000 in-window delta bought at day0's price (100) = 1,000 more units.
            // (10,000 + 1,000) * 100 = 1,100,000.
            // The bug would instead show 600,000 (the full cumulative cost bought once at day0's
            // price: 600,000 / 100 = 6,000 units * 100 = 600,000).
            Assert.Equal(1_100_000m, result.Benchmarks.Single().ValuesTWD[day0Idx]);
        }

        [Fact]
        public async Task GetBenchmarkComparison_MidWindowContribution_IsNotCountedAsReturn()
        {
            // Regression: the reported "+31.31% but I didn't earn that" case. Returns were a plain
            // (last - first) / first over the value curve, so money paid IN grew the curve and was
            // reported as performance. Here the price never moves and the portfolio doubles purely
            // by contribution — the only correct return is 0%.
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 100_000,
                });
            await SeedSnapshotAsync(day1,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 200, CostBasis = 200_000,
                });
            // Flat prices on both sides for the whole window — nothing was earned by either.
            SetupStockProvider("2330", MarketRegion.TW, (day0, 1000m), (day1, 1000m));
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 100m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            // The value curve still doubles — that part is real and worth plotting.
            var day0Idx = result.Dates.IndexOf(day0.ToString("yyyy-MM-dd"));
            var day1Idx = result.Dates.IndexOf(day1.ToString("yyyy-MM-dd"));
            Assert.Equal(100_000m, result.PortfolioTWD[day0Idx]);
            Assert.Equal(200_000m, result.PortfolioTWD[day1Idx]);

            // ...but neither side earned anything, so both returns must be flat.
            Assert.Equal(0m, result.PortfolioReturnPct);
            Assert.Equal(0m, result.Benchmarks.Single().ReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_PriceMoveAfterContribution_IsCountedAsReturn()
        {
            // The complement of the test above: a genuine price move must still be reported, and
            // must not be diluted by the contribution that preceded it.
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 100_000,
                });
            await SeedSnapshotAsync(day1,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 200, CostBasis = 200_000,
                });
            // Day 2 onward: price rises 10% with no further contribution.
            SetupStockProvider("2330", MarketRegion.TW, (day0, 1000m), (day1, 1000m), (Weekday(2), 1100m));
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 100m), (Weekday(2), 100m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.Equal(10m, result.PortfolioReturnPct);
            Assert.Equal(0m, result.Benchmarks.Single().ReturnPct); // benchmark price was flat
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

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

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

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

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

            var result = await _benchmark.GetBenchmarkComparisonAsync(
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

            var result = await _benchmark.GetBenchmarkComparisonAsync(
                TestUserId, days: 30, benchmarkSymbols: ["0050", "NOPE"]);

            var benchmark = Assert.Single(result.Benchmarks);
            Assert.Equal("0050", benchmark.Symbol);
        }

        [Fact]
        public async Task GetBenchmarkComparison_NoSnapshots_ReturnsEmptyResultWithRequestedSymbols()
        {
            var result = await _benchmark.GetBenchmarkComparisonAsync(
                TestUserId, days: 30, benchmarkSymbols: ["0050", "VT"]);

            Assert.Empty(result.Dates);
            Assert.Equal(["0050", "VT"], result.Benchmarks.Select(b => b.Symbol));
        }

        [Fact]
        public async Task GetBenchmarkComparison_PortfolioDividend_CountsAsReturnNotLoss()
        {
            // The 高股息 case. A distribution knocks the price down by roughly its own size on the
            // ex-date. Counting price only, the payout reads as a 5% loss even though the holder is
            // exactly as wealthy — and the comparison is then scored against a lower-yielding
            // benchmark that never takes that hit. Total return must show this as flat.
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "0056", Name = "高股息",
                    Market = "TW", Currency = "TWD", Quantity = 1000, CostBasis = 100_000,
                });
            SetupStockProvider("0056", MarketRegion.TW, (day0, 100m), (day1, 95m));
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 100m));
            SetupDividends("0056", (day1, 5m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            // Price alone would be (95,000 - 100,000) / 100,000 = -5%.
            Assert.Equal(0m, result.PortfolioReturnPct);
            Assert.Equal(0m, result.Benchmarks.Single().ReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_BenchmarkDividend_CountsForTheBenchmarkToo()
        {
            // The mirror image: crediting only the portfolio's dividends would hand it an unearned
            // edge over any benchmark that also distributes. 0050 pays too, and must get credit.
            var day0 = Weekday(0);
            var day1 = Weekday(1);
            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 100_000,
                });
            SetupStockProvider("2330", MarketRegion.TW, (day0, 1000m), (day1, 1000m));
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m), (day1, 95m));
            SetupDividends("0050", (day1, 5m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.Equal(0m, result.PortfolioReturnPct);
            Assert.Equal(0m, result.Benchmarks.Single().ReturnPct); // not -5%
        }

        [Fact]
        public async Task GetBenchmarkComparison_ExDateOnNonTradingDay_IsStillCounted()
        {
            // Ex-dates that land on a day the loop never records (weekend here, but the same path
            // covers a day where some symbol has no price) must accrue to the next recorded day
            // rather than vanish from the return.
            var day0 = Weekday(0);                 // Monday
            var saturday = AnchorMonday.AddDays(5);
            Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek); // guard the fixture's own premise

            await SeedSnapshotAsync(day0,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "0056", Name = "高股息",
                    Market = "TW", Currency = "TWD", Quantity = 1000, CostBasis = 100_000,
                });
            // Price never moves, so any non-zero return can only have come from the dividend.
            SetupStockProvider("0056", MarketRegion.TW, (day0, 100m));
            SetupStockProvider("0050", MarketRegion.TW, (day0, 100m));
            SetupDividends("0056", (saturday, 5m));

            var result = await _benchmark.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            // 5,000 of income on a flat 100,000 position.
            Assert.Equal(5m, result.PortfolioReturnPct);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Give one symbol a dividend history; every other symbol keeps the empty default.</summary>
        private void SetupDividends(string symbol, params (DateOnly ExDate, decimal AmountPerUnit)[] records)
        {
            _dividendData
                .Setup(x => x.GetStockDividendsAsync(symbol, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(records.Select(r => new DividendRecord(r.ExDate, r.AmountPerUnit, "TWD")).ToList());
        }

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

        [Fact]
        public void TheBenchmarks_AreAtMostWhatThePageOffers()
        {
            // Each is a deep history through the exchange's shared lane, so hundreds in one URL are not drawn (D-165).
            var many = Enumerable.Range(0, 600).Select(i => (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(["1000", "1001", "1002", "1003"], BenchmarkComparisonService.ResolveBenchmarkSymbols(many));
            Assert.Equal(BenchmarkComparisonService.MaxBenchmarks, BenchmarkComparisonService.ResolveBenchmarkSymbols(many).Count);
            Assert.Equal(["QQQ"], BenchmarkComparisonService.ResolveBenchmarkSymbols([new string('X', 41), " qqq ", "QQQ"]));
            Assert.Equal(["0050"], BenchmarkComparisonService.ResolveBenchmarkSymbols([]));
        }
    }
}
