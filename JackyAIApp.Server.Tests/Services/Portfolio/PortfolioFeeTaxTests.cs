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
    /// Covers the estimated-sell-cost deduction added to TW STOCK unrealized P&amp;L: brokerage fee
    /// (rate × caller-supplied discount) always applies, transaction tax rate depends on
    /// instrument type (0.3% individual stock / 0.1% TW-ETF "00"-prefix / 0% bond-ETF via
    /// PortfolioExposure's exposureFactor == 0). The discount rate is a client-supplied
    /// GetPortfolioSummaryAsync parameter (not server-persisted — the frontend keeps it in
    /// localStorage), defaulting to 2.8折 when omitted. US stocks and funds are untouched.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioFeeTaxTests : IDisposable
    {
        private const string TestUserId = "fee-tax-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly PortfolioService _service;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;

        public PortfolioFeeTaxTests()
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
            var usageLimitService = new Mock<IUsageLimitService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioService(
                _dbContext,
                _providerFactory.Object,
                fundData.Object,
                dividendData.Object,
                categoryService.Object,
                twListedCompanies: null!,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                usageLimitService.Object,
                NullLogger<PortfolioService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetSummary_IndividualTwStock_DeductsFeeAndFullTax()
        {
            SetupStockPrice("2330", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // value = 100,000. fee = floor(100,000 * 0.001425 * 0.28) = 39.
            // tax (individual stock, not "00"-prefixed) = floor(100,000 * 0.003) = 300.
            var position = Assert.Single(summary.Positions);
            Assert.Equal(339m, position.EstimatedSellCostTWD);
            Assert.Equal(10_000m - 339m, position.UnrealizedPnlTWD);
            Assert.Equal(339m, summary.EstimatedSellCostTWD);
        }

        [Fact]
        public async Task GetSummary_TwEtfWithZeroPrefix_UsesReducedEtfTaxRate()
        {
            SetupStockPrice("0050", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "0050", Name = "元大台灣50",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Same value/fee as the individual-stock case, but tax = floor(100,000 * 0.001) = 100.
            var position = Assert.Single(summary.Positions);
            Assert.Equal(39m + 100m, position.EstimatedSellCostTWD);
            Assert.Equal(10_000m - 139m, position.UnrealizedPnlTWD);
        }

        [Fact]
        public async Task GetSummary_BondEtf_TaxExemptButFeeStillApplies()
        {
            SetupStockPrice("00679B", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "00679B", Name = "元大美債20年",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Bond ETF (code suffix "B" => PortfolioExposure.Factor == 0) is tax-exempt —
            // only the 39 brokerage fee is deducted, no transaction tax.
            var position = Assert.Single(summary.Positions);
            Assert.Equal(39m, position.EstimatedSellCostTWD);
            Assert.Equal(10_000m - 39m, position.UnrealizedPnlTWD);
        }

        [Fact]
        public async Task GetSummary_NoRateSupplied_UsesDefaultDiscountRate()
        {
            SetupStockPrice("2330", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId); // feeDiscountRate defaults to 0.28m

            var position = Assert.Single(summary.Positions);
            Assert.Equal(339m, position.EstimatedSellCostTWD); // matches GetSummary_IndividualTwStock_DeductsFeeAndFullTax
        }

        [Fact]
        public async Task GetSummary_CustomDiscountRate_AffectsFeeCalculation()
        {
            SetupStockPrice("2330", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId, feeDiscountRate: 1m); // no discount

            // Full-rate fee (no discount): floor(100,000 * 0.001425) = 142, plus 300 tax = 442.
            var position = Assert.Single(summary.Positions);
            Assert.Equal(442m, position.EstimatedSellCostTWD);
        }

        [Fact]
        public async Task GetSummary_OutOfRangeRate_IsClampedRatherThanRejected()
        {
            SetupStockPrice("2330", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            // 5 clamps to 1 (same result as the "no discount" case above).
            var overRate = await _service.GetPortfolioSummaryAsync(TestUserId, feeDiscountRate: 5m);
            Assert.Equal(442m, Assert.Single(overRate.Positions).EstimatedSellCostTWD);

            // -1 clamps to 0.01: fee = floor(100,000 * 0.001425 * 0.01) = 1, plus 300 tax = 301.
            var underRate = await _service.GetPortfolioSummaryAsync(TestUserId, feeDiscountRate: -1m);
            Assert.Equal(301m, Assert.Single(underRate.Positions).EstimatedSellCostTWD);
        }

        [Fact]
        public async Task GetSummary_UsStock_NoSellCostDeducted()
        {
            var provider = new Mock<IMarketDataProvider>();
            var aaplData = new MarketData
            {
                StockCode = "AAPL",
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = 100m }],
            };
            provider.Setup(p => p.FetchAsync("AAPL", It.IsAny<CancellationToken>())).ReturnsAsync(aaplData);
            // GetPortfolioSummaryAsync only needs the latest price — shallow fetch.
            provider.Setup(p => p.FetchLatestAsync("AAPL", It.IsAny<CancellationToken>())).ReturnsAsync(aaplData);
            _providerFactory.Setup(f => f.DetectRegion("AAPL")).Returns(MarketRegion.US);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.US)).Returns(provider.Object);

            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "AAPL", Name = "Apple",
                Market = "US", Currency = "USD", CostCurrency = "USD", Quantity = 10, CostBasis = 900m,
            });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            var position = Assert.Single(summary.Positions);
            Assert.Equal(0m, position.EstimatedSellCostTWD);
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
