using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
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
    /// Pins how the two per-day valuators — ValueAsOfDate (drawdown, day change) and
    /// InvestedValueAsOfDate (benchmark comparison) — look a price up "as of" a day, at the level of
    /// the public endpoints. The other Portfolio test classes only drive these with plain stock
    /// histories; the fund branch, the "no price yet" / "price of zero" distinction and same-day
    /// duplicate bars had no coverage, and they are exactly what changed when the lookup moved from
    /// a per-cell filter + sort to AsOfPriceSeries. Every expectation here is the behaviour from
    /// before that change, so they hold on either implementation.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioAsOfValuationTests : IDisposable
    {
        private const string TestUserId = "asof-valuation-test-user";

        // Monday, fixed — not derived from the real clock (see PortfolioDrawdownTests).
        private static readonly DateOnly AnchorMonday = new(2026, 7, 20);
        private static readonly DateOnly FixedToday = AnchorMonday.AddDays(20);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly Mock<IFundDataService> _fundData = new();
        private readonly Dictionary<MarketRegion, Mock<IMarketDataProvider>> _regionProviders = new();
        private readonly PortfolioService _service;

        public PortfolioAsOfValuationTests()
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

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                _fundData.Object,
                dividendData.Object,
                new Mock<IStockCategoryService>().Object,
                twListedCompanies: null!,
                new Mock<IHttpClientFactory>().Object,
                new MemoryCache(new MemoryCacheOptions()),
                new Mock<IOpenAIPromptService>().Object,
                NullLogger<PortfolioService>.Instance,
                new FakeTimeProvider(new DateTimeOffset(FixedToday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        public void Dispose() => _dbContext.Dispose();

        // ── ValueAsOfDate, via the drawdown endpoint ────────────────────────────────────────────

        [Fact]
        public async Task Drawdown_Fund_IsValuedAtTheLatestNavOnOrBeforeEachDay()
        {
            // UnitPrice 1 is a decoy: a NAV exists for every day here, so it must never be used.
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 1));
            SetupFund("FND1", "TWD", (Weekday(0), 100m), (Weekday(2), 80m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(1000m, TotalOn(result, Weekday(0)));
            Assert.Equal(1000m, TotalOn(result, Weekday(1))); // no NAV published: yesterday's carries
            Assert.Equal(800m, TotalOn(result, Weekday(2)));
            Assert.Equal(-20m, result.MaxDrawdownPct);
            Assert.Equal(Key(Weekday(2)), result.TroughDate);
        }

        [Fact]
        public async Task Drawdown_Fund_ConvertsAtTheNavCurrencyNotThePositionCurrency()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 1, currency: "TWD"));
            SetupFund("FND1", "USD", (Weekday(0), 100m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(100m * 10 * 32m, TotalOn(result, Weekday(0))); // snapshot rate is 32
        }

        [Fact]
        public async Task Drawdown_FundWithNoNavHistoryAtAll_FallsBackToTheSavedUnitPrice()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 50));
            // _fundData has no setup for FND1: GetFundNavHistoryAsync yields null.

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(500m, TotalOn(result, Weekday(0)));
            Assert.Equal(500m, TotalOn(result, Weekday(2)));
        }

        [Fact]
        public async Task Drawdown_FundBeforeItsFirstNav_FallsBackToTheSavedUnitPrice()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 50));
            SetupFund("FND1", "TWD", (Weekday(2), 100m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(500m, TotalOn(result, Weekday(0)));
            Assert.Equal(500m, TotalOn(result, Weekday(1)));
            Assert.Equal(1000m, TotalOn(result, Weekday(2)));
        }

        [Fact]
        public async Task Drawdown_StockBeforeItsFirstPrice_HasNoPointsForThoseDays()
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100));
            SetupStockBars("2330", Bar(Weekday(2), 1000m), Bar(Weekday(3), 1100m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(Key(Weekday(2)), result.Points[0].Date);
        }

        [Fact]
        public async Task Drawdown_StockWhoseLatestBarIsZero_DropsThatDayInsteadOfCarryingTheEarlierClose()
        {
            // "No price" and "a price of 0" differ: the 0 bar IS the as-of price for Weekday(1), and
            // an unpriceable position drops the whole day rather than reading as the earlier 1000.
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100));
            SetupStockBars("2330", Bar(Weekday(0), 1000m), Bar(Weekday(1), 0m), Bar(Weekday(2), 1000m));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            var dates = result.Points.Select(p => p.Date).ToList();
            Assert.Contains(Key(Weekday(0)), dates);
            Assert.DoesNotContain(Key(Weekday(1)), dates);
            Assert.Contains(Key(Weekday(2)), dates);
        }

        [Fact]
        public async Task Drawdown_StockWithNoMarketDataAtAll_ProducesNoPoints()
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("9999", quantity: 100));
            // No provider setup for 9999: the fetch yields nothing and the symbol is absent from the map.

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Empty(result.Points);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Drawdown_TwoBarsOnTheSameDay_TheLaterTimestampIsTheDaysPrice(bool laterListedFirst)
        {
            // Yahoo bars carry a time of day, so one calendar day can appear twice.
            var morning = new DailyPrice { Date = Weekday(1).ToDateTime(new TimeOnly(9, 0)), Close = 900m };
            var evening = new DailyPrice { Date = Weekday(1).ToDateTime(new TimeOnly(13, 30)), Close = 700m };
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100));
            SetupStockBars("2330",
                [Bar(Weekday(0), 1000m), .. laterListedFirst ? new[] { evening, morning } : new[] { morning, evening }]);

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(70_000m, TotalOn(result, Weekday(1)));
        }

        [Theory]
        [InlineData(800, 600, 80_000)]
        [InlineData(600, 800, 60_000)]
        public async Task Drawdown_TwoBarsWithTheSameTimestamp_TheOneListedFirstIsTheDaysPrice(
            int firstClose, int secondClose, int expectedTotal)
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100));
            SetupStockBars("2330", Bar(Weekday(0), 1000m), Bar(Weekday(1), firstClose), Bar(Weekday(1), secondClose));

            var result = await _service.GetDrawdownAnalysisAsync(TestUserId, days: 30);

            Assert.Equal(expectedTotal, TotalOn(result, Weekday(1)));
        }

        // ── InvestedValueAsOfDate, via the benchmark-comparison endpoint ────────────────────────

        [Fact]
        public async Task Benchmark_Fund_IsValuedAtTheLatestNavOnOrBeforeEachDay()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 1, costBasis: 10_000));
            SetupFund("FND1", "TWD", (Weekday(0), 1000m), (Weekday(2), 1100m));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Equal(10_000m, PortfolioOn(result, Weekday(0)));
            Assert.Equal(10_000m, PortfolioOn(result, Weekday(1)));
            Assert.Equal(11_000m, PortfolioOn(result, Weekday(2)));
        }

        [Fact]
        public async Task Benchmark_FundBeforeItsFirstNav_FallsBackToTheSavedUnitPrice()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 900, costBasis: 10_000));
            SetupFund("FND1", "TWD", (Weekday(2), 1100m));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Equal(9_000m, PortfolioOn(result, Weekday(0)));
            Assert.Equal(9_000m, PortfolioOn(result, Weekday(1)));
            Assert.Equal(11_000m, PortfolioOn(result, Weekday(2)));
        }

        [Fact]
        public async Task Benchmark_Fund_ConvertsAtTheNavCurrency()
        {
            await SeedSnapshotAsync(Weekday(0), FundPosition("FND1", quantity: 10, unitPrice: 1, costBasis: 10_000));
            SetupFund("FND1", "USD", (Weekday(0), 10m));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Equal(10m * 10 * 32m, PortfolioOn(result, Weekday(0)));
        }

        [Fact]
        public async Task Benchmark_StockBeforeItsFirstPrice_HasNoPointsForThoseDays()
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100, costBasis: 90_000));
            SetupStockBars("2330", Bar(Weekday(2), 1000m));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Equal(Key(Weekday(2)), result.Dates[0]);
        }

        [Fact]
        public async Task Benchmark_StockWhoseLatestBarIsZero_DropsThatDay()
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100, costBasis: 90_000));
            SetupStockBars("2330", Bar(Weekday(0), 1000m), Bar(Weekday(1), 0m), Bar(Weekday(2), 1000m));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Contains(Key(Weekday(0)), result.Dates);
            Assert.DoesNotContain(Key(Weekday(1)), result.Dates);
            Assert.Contains(Key(Weekday(2)), result.Dates);
        }

        [Fact]
        public async Task Benchmark_StockWithNoMarketDataAtAll_ProducesNoDates()
        {
            await SeedSnapshotAsync(Weekday(0), StockPosition("9999", quantity: 100, costBasis: 90_000));
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Empty(result.Dates);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Benchmark_TwoStockBarsOnTheSameDay_TheLaterTimestampIsTheDaysPrice(bool laterListedFirst)
        {
            var morning = new DailyPrice { Date = Weekday(1).ToDateTime(new TimeOnly(9, 0)), Close = 900m };
            var evening = new DailyPrice { Date = Weekday(1).ToDateTime(new TimeOnly(13, 30)), Close = 700m };
            await SeedSnapshotAsync(Weekday(0), StockPosition("2330", quantity: 100, costBasis: 90_000));
            SetupStockBars("2330",
                [Bar(Weekday(0), 1000m), .. laterListedFirst ? new[] { evening, morning } : new[] { morning, evening }]);
            SetupStockBars("0050", Bar(Weekday(0), 100m));

            var result = await _service.GetBenchmarkComparisonAsync(TestUserId, days: 30, ["0050"]);

            Assert.Equal(70_000m, PortfolioOn(result, Weekday(1)));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────

        private static string Key(DateOnly d) => d.ToString("yyyy-MM-dd");

        private static decimal TotalOn(DrawdownAnalysisDto result, DateOnly date) =>
            result.Points.Single(p => p.Date == Key(date)).TotalTWD;

        private static decimal PortfolioOn(BenchmarkComparisonDto result, DateOnly date) =>
            result.PortfolioTWD[result.Dates.IndexOf(Key(date))];

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

        private static DailyPrice Bar(DateOnly date, decimal close) =>
            new() { Date = date.ToDateTime(TimeOnly.MinValue), Close = close };

        private static HoldingPosition StockPosition(string symbol, decimal quantity, decimal? costBasis = null) => new()
        {
            AssetType = AssetType.STOCK, Symbol = symbol, Name = symbol,
            Market = "TW", Currency = "TWD", Quantity = quantity, CostBasis = costBasis,
        };

        private static HoldingPosition FundPosition(
            string fundId, decimal quantity, decimal unitPrice, string currency = "TWD", decimal? costBasis = null) => new()
        {
            AssetType = AssetType.FUND, Symbol = fundId, Name = fundId,
            Currency = currency, Quantity = quantity, UnitPrice = unitPrice, CostBasis = costBasis,
        };

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

        private void SetupFund(string fundId, string currency, params (DateOnly Date, decimal Close)[] navs) =>
            _fundData.Setup(f => f.GetFundNavHistoryAsync(fundId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory(currency,
                    [.. navs.Select(n => new PricePoint { Date = Key(n.Date), Close = n.Close })]));

        // One provider mock per region (GetMarketDataProvider is keyed by region, not symbol), one
        // FetchAsync setup per symbol — see PortfolioBenchmarkComparisonTests for why.
        private void SetupStockBars(string symbol, params DailyPrice[] bars)
        {
            const MarketRegion region = MarketRegion.TW;
            _providerFactory.Setup(x => x.DetectRegion(symbol)).Returns(region);

            if (!_regionProviders.TryGetValue(region, out var provider))
            {
                provider = new Mock<IMarketDataProvider>();
                _regionProviders[region] = provider;
                _providerFactory.Setup(x => x.GetMarketDataProvider(region)).Returns(provider.Object);
            }

            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = symbol, HistoricalPrices = [.. bars] });
        }
    }
}
