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
    /// Covers the LIABILITY asset type added for debt tracking (PORTFOLIO_LIABILITY_PLAN.md):
    /// net worth nets out debt, exposure ratio uses the debt-adjusted denominator, and debt
    /// never leaks into cost-basis/P&amp;L/XIRR since it carries no cost basis.
    /// </summary>
    public class PortfolioLiabilityTests : IDisposable
    {
        private const string TestUserId = "liability-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly PortfolioService _service;

        public PortfolioLiabilityTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            // No dividends in any of these scenarios — liability tests don't cover total-return math.
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            // Loose mock with no CreateClient() setup returns null, GetStringAsync throws,
            // and FetchUsdTwdRateAsync's catch-all falls back to a fixed rate — no real network call.
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
                stockService: null!, // only used by an unrelated stock-search path, never exercised here
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
        public async Task GetSummary_CashAndLiability_NetsWorthAndReportsLiabilitySeparately()
        {
            await SeedSnapshotAsync(
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 1_000_000 },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 300_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(700_000m, summary.TotalNetWorthTWD);
            Assert.Equal(300_000m, summary.LiabilityTWD);
            Assert.Equal(1_000_000m, summary.CashTWD);
        }

        [Fact]
        public async Task GetSummary_StockCashAndLiability_ExposureRatioUsesDebtAdjustedNetWorth()
        {
            SetupStockPrice("2330", price: 1000m);

            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 1000,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 500_000 },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 300_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Net worth = 1,000,000 (stock) + 500,000 (cash) - 300,000 (liability) = 1,200,000
            Assert.Equal(1_200_000m, summary.TotalNetWorthTWD);
            // Exposure is only the stock (factor 1) = 1,000,000; liability contributes 0 to the numerator
            // but does shrink the denominator, so the ratio is higher than if debt were ignored.
            Assert.Equal(1_000_000m, summary.MarketExposureTWD);
            Assert.Equal(83.3m, summary.ExposureRatioPct);
        }

        [Fact]
        public async Task GetSummary_LiabilityHasNoCostBasis_DoesNotAffectPnlOrCostBasisTotals()
        {
            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 300_000 });
            SetupStockPrice("2330", price: 1000m); // 100,000 value vs 90,000 cost => +10,000 P&L

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(90_000m, summary.CostBasisTWD);
            Assert.Equal(10_000m, summary.UnrealizedPnlTWD);
            // The liability position itself never contributed a CostBasisTWD/PnL entry.
            var liabilityPosition = Assert.Single(summary.Positions, p => p.AssetType == AssetType.LIABILITY);
            Assert.Null(liabilityPosition.CostBasisTWD);
            Assert.Null(liabilityPosition.UnrealizedPnlTWD);
        }

        [Fact]
        public async Task SaveHoldings_LiabilityRowWithoutSymbol_IsKept()
        {
            await _service.SaveHoldingsAsync(TestUserId, new SaveHoldingsRequest
            {
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Positions =
                [
                    new PositionInput { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 300_000 },
                ],
            });

            var saved = await _dbContext.HoldingSnapshots
                .Include(s => s.Positions)
                .SingleAsync(s => s.UserId == TestUserId);

            var position = Assert.Single(saved.Positions);
            Assert.Equal(AssetType.LIABILITY, position.AssetType);
            Assert.Equal(300_000m, position.Quantity);
            Assert.Null(position.CostBasis);
            Assert.Null(position.CostCurrency);
        }

        [Fact]
        public async Task SaveHoldings_LiabilityRowWithZeroQuantity_IsFiltered()
        {
            await _service.SaveHoldingsAsync(TestUserId, new SaveHoldingsRequest
            {
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Positions =
                [
                    new PositionInput { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 0 },
                ],
            });

            var saved = await _dbContext.HoldingSnapshots
                .Include(s => s.Positions)
                .SingleAsync(s => s.UserId == TestUserId);

            Assert.Empty(saved.Positions);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task SeedSnapshotAsync(params HoldingPosition[] positions)
        {
            _dbContext.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UsdTwdRate = 32m,
                Positions = positions.ToList(),
            });
            await _dbContext.SaveChangesAsync();
        }

        private void SetupStockPrice(string symbol, decimal price)
        {
            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = symbol,
                    HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = price }],
                });
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }
    }
}
