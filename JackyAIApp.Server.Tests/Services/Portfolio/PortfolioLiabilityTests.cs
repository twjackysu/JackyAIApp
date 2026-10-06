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
    [Collection(SingleFlightCollection.Name)]
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
            var usageLimitService = new Mock<IUsageLimitService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioService(
                _dbContext,
                providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                twListedCompanies: null!, // only used by an unrelated stock-search path, never exercised here
                httpClientFactory.Object,
                cache,
                openAI.Object,
                usageLimitService.Object,
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
            // but does shrink the denominator, so the multiple is higher than if debt were ignored.
            Assert.Equal(1_000_000m, summary.MarketExposureTWD);
            // 水位 divides by GROSS liquid assets (1,500,000) — debt not netted out.
            Assert.Equal(66.7m, summary.ExposureOfLiquidAssetsPct);
            // 槓桿 divides by NET (1,200,000) — under 1x, so this position isn't actually levered.
            Assert.Equal(0.83m, summary.LiquidExposureMultiple);
            // Reciprocal of the multiple: a 120% market-wide fall would be needed to zero out the
            // 1,200,000 of liquid net worth backing 1,000,000 of exposure.
            Assert.Equal(120m, summary.DrawdownBufferPct);
        }

        [Fact]
        public async Task GetSummary_LeveragedAndInsolvent_LeverageMultipleIsNullButBufferGoesNegative()
        {
            // The exact scenario that motivated splitting these gauges apart: debt exceeds liquid
            // assets, so liquid net worth is negative. exposure ÷ equity would produce a
            // meaningless negative multiple that reads as *safer* than a large positive one, so
            // it's suppressed entirely; the buffer stays continuous and correctly reads negative
            // (already past zero). 水位 divides by gross assets, which can't go negative, so it
            // keeps reporting normally throughout.
            SetupStockPrice("00631L", price: 100m);

            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "00631L", Name = "元大台灣50正2",
                    Market = "TW", Currency = "TWD", Quantity = 5_000,
                },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "貸款", Currency = "TWD", Quantity = 1_500_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Liquid net worth = 500,000 (stock) - 1,500,000 (debt) = -1,000,000.
            // 00631L carries a 2x exposure factor, so exposure = 1,000,000.
            Assert.Equal(1_000_000m, summary.MarketExposureTWD);
            Assert.Null(summary.LiquidExposureMultiple);
            Assert.Equal(-100m, summary.DrawdownBufferPct); // -1,000,000 / 1,000,000 * 100
            // Gross liquid assets are still 500,000, so 水位 is a healthy-looking 200% — which is
            // exactly why it can't stand alone as the risk gauge: it says nothing about the debt.
            Assert.Equal(200m, summary.ExposureOfLiquidAssetsPct);
        }

        [Fact]
        public async Task GetSummary_NoMarketExposure_BufferIsNullRatherThanDivideByZero()
        {
            // The buffer's only degenerate case — and unlike the ratio's, it sits at the *safest*
            // possible state (nothing at risk), so the UI can render it as 無市場曝險.
            await SeedSnapshotAsync(
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 500_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(0m, summary.MarketExposureTWD);
            Assert.Null(summary.DrawdownBufferPct);
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
            SetupStockPrice("2330", price: 1000m); // 100,000 value vs 90,000 cost => +10,000 raw P&L

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(90_000m, summary.CostBasisTWD);
            // Raw +10,000 minus estimated sell-side cost at the default 2.8折 discount: fee
            // floor(100,000 * 0.001425 * 0.28) = 39, tax (individual TW stock, not "00"-prefixed
            // ETF, exposureFactor != 0) floor(100,000 * 0.003) = 300 => 10,000 - 339 = 9,661.
            Assert.Equal(9_661m, summary.UnrealizedPnlTWD);
            // The liability position itself never contributed a CostBasisTWD/PnL entry.
            var liabilityPosition = Assert.Single(summary.Positions, p => p.AssetType == AssetType.LIABILITY);
            Assert.Null(liabilityPosition.CostBasisTWD);
            Assert.Null(liabilityPosition.UnrealizedPnlTWD);
        }

        [Fact]
        public async Task GetSummary_UnrealizedPnlPct_IsPctOfLiquidGrossAssetsNotCostBasis()
        {
            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 900_000 },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 300_000 });
            SetupStockPrice("2330", price: 1000m); // 100,000 stock value vs 90,000 cost

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Gross liquid assets = 100,000 (stock) + 900,000 (cash) = 1,000,000 — excludes real
            // estate (none here) and is NOT net of the 300,000 liability.
            Assert.Equal(1_000_000m, summary.LiquidGrossAssetsTWD);
            // UnrealizedPnlTWD = 9,661 (see the cost-basis test above for the fee/tax breakdown).
            // % is against LiquidGrossAssetsTWD (1,000,000), not CostBasisTWD (90,000) — otherwise
            // this would read ~10.73% instead of ~0.97%.
            Assert.Equal(9_661m, summary.UnrealizedPnlTWD);
            Assert.Equal(0.97m, summary.UnrealizedPnlPct);
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
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = price }],
            };
            provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            // GetPortfolioSummaryAsync only needs the latest price — shallow fetch.
            provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }
    }
}
