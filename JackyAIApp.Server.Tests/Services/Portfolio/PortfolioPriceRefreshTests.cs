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
    /// Covers RefreshPositionPriceAsync — the manual "reload" retry for a position stuck showing
    /// "報價失敗" (e.g. a TW symbol whose market type was wrongly resolved once and never
    /// re-checked since — see <see cref="PersistentTwHistoricalProvider.InvalidateAsync"/>).
    /// </summary>
    public class PortfolioPriceRefreshTests : IDisposable
    {
        private const string TestUserId = "price-refresh-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly PortfolioService _service;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory;

        public PortfolioPriceRefreshTests()
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
                stockService: null!,
                httpClientFactory.Object,
                cache,
                openAI.Object,
                usageLimitService.Object,
                NullLogger<PortfolioService>.Instance);
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task RefreshPositionPrice_OwnedStock_InvalidatesProviderAndReturnsFreshSummary()
        {
            var provider = SetupStockPrice("00864B", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "00864B", Name = "中信美國公債0-1",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            var summary = await _service.RefreshPositionPriceAsync(TestUserId, "00864B");

            provider.Verify(p => p.InvalidateAsync("00864B", It.IsAny<CancellationToken>()), Times.Once);
            var position = Assert.Single(summary.Positions);
            Assert.Equal(1000m, position.CurrentPrice);
            Assert.False(position.PriceFetchFailed);
        }

        [Fact]
        public async Task RefreshPositionPrice_SymbolNotHeldByUser_NeverInvalidatesAndStillReturnsSummary()
        {
            var provider = SetupStockPrice("2330", price: 500m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", Quantity = 10, CostBasis = 4_000,
            });

            // "9999" isn't a position this user holds — refreshing it must be a no-op, not an
            // arbitrary-symbol external-API trigger.
            var summary = await _service.RefreshPositionPriceAsync(TestUserId, "9999");

            provider.Verify(p => p.InvalidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            var position = Assert.Single(summary.Positions);
            Assert.Equal("2330", position.Symbol);
        }

        [Fact]
        public async Task RefreshPositionPrice_LowercaseSymbol_StillMatchesUppercasePersistedHolding()
        {
            var provider = SetupStockPrice("00864B", price: 1000m);
            await SeedSnapshotAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "00864B", Name = "中信美國公債0-1",
                Market = "TW", Currency = "TWD", Quantity = 100, CostBasis = 90_000,
            });

            await _service.RefreshPositionPriceAsync(TestUserId, "00864b");

            provider.Verify(p => p.InvalidateAsync("00864B", It.IsAny<CancellationToken>()), Times.Once);
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

        private Mock<IMarketDataProvider> SetupStockPrice(string symbol, decimal price)
        {
            var provider = new Mock<IMarketDataProvider>();
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow, Close = price }],
            };
            provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            provider.Setup(p => p.InvalidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
            return provider;
        }
    }
}
