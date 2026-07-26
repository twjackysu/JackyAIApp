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
    /// Covers the REAL_ESTATE asset type: a manually-valued holding (no live price feed) that
    /// counts toward net worth but carries zero market exposure — added so a home-equity loan
    /// against real property doesn't make the exposure ratio look more leveraged than it is,
    /// without pulling the property's own value into the denominator (see
    /// [[portfolio-liability-scope-rule]] memory / 2026-07-24 conversation).
    /// </summary>
    public class PortfolioRealEstateTests : IDisposable
    {
        private const string TestUserId = "real-estate-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly PortfolioService _service;

        public PortfolioRealEstateTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var providerFactory = new Mock<IMarketDataProviderFactory>();
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
                providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                stockService: null!,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                creditService.Object,
                NullLogger<PortfolioService>.Instance);

            _providerFactory = providerFactory;
        }

        private readonly Mock<IMarketDataProviderFactory> _providerFactory;

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetSummary_RealEstateAndMortgage_CountsTowardNetWorthWithZeroExposure()
        {
            SetupStockPrice("2330", price: 1000m);

            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 1000,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 9_500_000 },
                new HoldingPosition
                {
                    AssetType = AssetType.REAL_ESTATE, Name = "自住房",
                    Currency = "TWD", Quantity = 16_000_000, ValuationMethod = "市價",
                },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "房屋增貸", Currency = "TWD", Quantity = 9_000_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Net worth = 1,000,000 (stock) + 9,500,000 (cash) + 16,000,000 (real estate)
            //           - 9,000,000 (liability) = 17,500,000
            Assert.Equal(17_500_000m, summary.TotalNetWorthTWD);
            Assert.Equal(16_000_000m, summary.RealEstateTWD);
            // Exposure is only the stock (factor 1) = 1,000,000 — real estate contributes 0 to the
            // numerator despite being a large position, which is the whole point of this feature:
            // without it, a heavily real-estate-backed net worth would make exposure ratio look
            // artificially low if real estate weren't counted, or artificially high if it were
            // counted toward net worth but not excluded from the exposure numerator.
            Assert.Equal(1_000_000m, summary.MarketExposureTWD);
            Assert.Equal(5.7m, summary.ExposureRatioPct); // 1,000,000 / 17,500,000 * 100, rounded
            // Excluding real estate from the denominator (liquid net worth = 1,500,000) shows the
            // liquid portfolio is actually far more leveraged than the "含不動產" ratio implies —
            // this is the whole point of exposing both numbers.
            Assert.Equal(66.7m, summary.LiquidExposureRatioPct); // 1,000,000 / 1,500,000 * 100, rounded
        }

        [Fact]
        public async Task GetSummary_NoRealEstate_LiquidExposureRatioMatchesRegularRatio()
        {
            SetupStockPrice("2330", price: 1000m);

            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 1000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // No real estate to exclude — both ratios collapse to the same value.
            Assert.Equal(summary.ExposureRatioPct, summary.LiquidExposureRatioPct);
        }

        [Fact]
        public async Task GetSummary_RealEstatePosition_HasNoCostBasisPnlOrPriceFailure()
        {
            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.REAL_ESTATE, Name = "自住房",
                    Currency = "TWD", Quantity = 16_000_000, ValuationMethod = "買價扣除折舊",
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            var position = Assert.Single(summary.Positions);
            Assert.Null(position.CostBasisTWD);
            Assert.Null(position.UnrealizedPnlTWD);
            Assert.False(position.PriceFetchFailed);
            Assert.Equal(16_000_000m, position.MarketValueTWD);
            Assert.Equal(0m, position.ExposureFactor);
            Assert.Equal("買價扣除折舊", position.ValuationMethod);
        }

        [Fact]
        public async Task SaveHoldings_RealEstateRow_PersistsValuationMethodAndDropsCostBasis()
        {
            await _service.SaveHoldingsAsync(TestUserId, new SaveHoldingsRequest
            {
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Positions =
                [
                    new PositionInput
                    {
                        AssetType = AssetType.REAL_ESTATE, Name = "自住房", Currency = "TWD",
                        Quantity = 16_000_000, ValuationMethod = "市價",
                        CostBasis = 12_000_000, // should be dropped — real estate has no cost-basis concept
                    },
                ],
            });

            var saved = await _dbContext.HoldingSnapshots
                .Include(s => s.Positions)
                .SingleAsync(s => s.UserId == TestUserId);

            var position = Assert.Single(saved.Positions);
            Assert.Equal(AssetType.REAL_ESTATE, position.AssetType);
            Assert.Equal(16_000_000m, position.Quantity);
            Assert.Equal("市價", position.ValuationMethod);
            Assert.Null(position.CostBasis);
            Assert.Null(position.CostCurrency);
        }

        [Fact]
        public async Task SaveHoldings_RealEstateRowWithoutSymbol_IsKept()
        {
            await _service.SaveHoldingsAsync(TestUserId, new SaveHoldingsRequest
            {
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Positions =
                [
                    new PositionInput { AssetType = AssetType.REAL_ESTATE, Name = "自住房", Currency = "TWD", Quantity = 16_000_000 },
                ],
            });

            var saved = await _dbContext.HoldingSnapshots
                .Include(s => s.Positions)
                .SingleAsync(s => s.UserId == TestUserId);

            Assert.Single(saved.Positions);
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupByAssetType_UsesPropertyKeyNotRealEstate()
        {
            // "Property", not "RealEstate": the latter key is reserved for PortfolioCategory.RealEstate
            // (REIT stocks/ETFs in category-groupBy mode) — a literal house must not collide with it.
            await SeedSnapshotAsync(AnchorMonday,
                new HoldingPosition
                {
                    AssetType = AssetType.REAL_ESTATE, Name = "自住房",
                    Currency = "TWD", Quantity = 16_000_000,
                });

            // Wide enough to always include today's date whatever weekday it happens to be — a
            // days:7 window starting from the snapshot date could land entirely on a skipped
            // weekend depending on which real day the suite runs on.
            var history = await _service.GetNetWorthHistoryAsync(TestUserId, days: 90);

            var lastPoint = history.Last();
            var group = Assert.Single(lastPoint.Groups);
            Assert.Equal("Property", group.Key);
            Assert.Equal(16_000_000m, group.ValueTWD);
        }

        [Fact]
        public async Task GetNetWorthHistory_GroupByCategory_UsesPropertyEnumMember()
        {
            await SeedSnapshotAsync(AnchorMonday,
                new HoldingPosition
                {
                    AssetType = AssetType.REAL_ESTATE, Name = "自住房",
                    Currency = "TWD", Quantity = 16_000_000,
                });

            var history = await _service.GetNetWorthHistoryAsync(TestUserId, days: 90, groupBy: NetWorthGroupBy.Category);

            var lastPoint = history.Last();
            var group = Assert.Single(lastPoint.Groups);
            Assert.Equal(nameof(PortfolioCategory.Property), group.Key);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // A fixed past Monday, far enough back that `days: 90` always reaches today regardless of
        // which real weekday the suite happens to run on (avoids the flakiness of seeding at
        // DateTime.UtcNow directly — if "today" is a Saturday/Sunday, GetNetWorthHistoryAsync skips
        // it, and a narrow window anchored on today alone can produce zero data points).
        private static readonly DateOnly AnchorMonday = MostRecentMonday(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-28));

        private static DateOnly MostRecentMonday(DateOnly from)
        {
            var d = from;
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
            return d;
        }

        private async Task SeedSnapshotAsync(params HoldingPosition[] positions) =>
            await SeedSnapshotAsync(DateOnly.FromDateTime(DateTime.UtcNow), positions);

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

        private void SetupStockPrice(string symbol, decimal price)
        {
            var provider = new Mock<IMarketDataProvider>();
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = price }],
            };
            provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }
    }
}
