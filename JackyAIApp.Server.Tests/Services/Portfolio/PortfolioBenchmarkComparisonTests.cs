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
    /// GetBenchmarkComparisonAsync simulates "what if every contribution had bought the benchmark
    /// instead" — driven by the exact same cost-basis-delta contribution proxy
    /// ComputeAnnualizedReturn (XIRR) already uses. These tests pin the unit-buying/selling math and
    /// the cost-tracked-only universe on both sides. Same fixed-past-Monday date pattern as the
    /// other portfolio history tests, for the same day-of-week-stability reason.
    /// </summary>
    public class PortfolioBenchmarkComparisonTests : IDisposable
    {
        private const string TestUserId = "benchmark-test-user";

        private static readonly DateOnly AnchorMonday = MostRecentMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-28));

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
                NullLogger<PortfolioService>.Instance);
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
            // "today", so the window has more than 2 points; the values matter, not the count.
            Assert.Equal("0050", result.BenchmarkSymbol);
            Assert.NotEmpty(result.Points);

            var day0 = result.Points[0];
            Assert.Equal(Weekday(0).ToString("yyyy-MM-dd"), day0.Date);
            Assert.Equal(100_000m, day0.PortfolioTWD); // 100 shares * 1000
            Assert.Equal(90_000m, day0.BenchmarkTWD);  // 900 units * 100

            var day1 = result.Points.Single(p => p.Date == Weekday(1).ToString("yyyy-MM-dd"));
            Assert.Equal(110_000m, day1.PortfolioTWD); // 100 shares * 1100
            Assert.Equal(99_000m, day1.BenchmarkTWD);  // 900 units * 110 (no new contribution)

            // Period return (last point in the window vs. first) — the last point mirrors day1's
            // values since both prices are flat after it: (110,000-100,000)/100,000 = 10%;
            // (99,000-90,000)/90,000 = 10%.
            Assert.Equal(10m, result.PortfolioReturnPct);
            Assert.Equal(10m, result.BenchmarkReturnPct);
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

            var point1 = result.Points.Single(p => p.Date == day1.ToString("yyyy-MM-dd"));
            // (500 + 250) units * 200 = 150,000.
            Assert.Equal(150_000m, point1.BenchmarkTWD);
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

            // Forward-fill produces many days from Weekday(0) through "today", all flat at 0 since
            // the untracked-cost position never contributes to either side.
            Assert.NotEmpty(result.Points);
            Assert.All(result.Points, p =>
            {
                Assert.Equal(0m, p.PortfolioTWD);
                Assert.Equal(0m, p.BenchmarkTWD);
            });
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

            Assert.Empty(result.Points);
            Assert.Null(result.PortfolioReturnPct);
            Assert.Null(result.BenchmarkReturnPct);
        }

        [Fact]
        public async Task GetBenchmarkComparison_NoSnapshots_ReturnsEmptyResult()
        {
            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30);

            Assert.Empty(result.Points);
            Assert.Equal("0050", result.BenchmarkSymbol);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static DateOnly MostRecentMonday(DateOnly from)
        {
            var d = from;
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
            return d;
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
    }
}
