using JackyAIApp.Server.Data;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
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
    /// Covers total (含息) return added by PORTFOLIO_DIVIDEND_PLAN.md: dividends are attributed
    /// to whatever quantity was actually held on their ex-date (via the snapshot timeline), converted
    /// with the live FX rate, and never leak in before the user's first snapshot.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioDividendTests : IDisposable
    {
        private const string TestUserId = "dividend-test-user";

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IDividendService> _dividendData;
        private readonly PortfolioService _service;

        public PortfolioDividendTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var providerFactory = new Mock<IMarketDataProviderFactory>();
            var fundData = new Mock<IFundDataService>();
            _dividendData = new Mock<IDividendService>();
            // Default: no dividends anywhere unless a test overrides for a specific symbol.
            _dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            _dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());

            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
            var categoryService = new Mock<IStockCategoryService>();

            _service = new PortfolioServiceBuilder(_dbContext)
            {
                Providers = providerFactory.Object,
                FundData = fundData.Object,
                Dividends = _dividendData.Object,
                Categories = categoryService.Object,
                HttpClientFactory = httpClientFactory.Object,
                Cache = cache,
            }.Build();
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetSummary_DividendOnExDateWithinHoldingPeriod_AccumulatesByQuantityHeld()
        {
            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });

            _dividendData.Setup(x => x.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(snapshotDate.AddDays(5), AmountPerUnit: 5m, Currency: "TWD"),
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            var position = Assert.Single(summary.Positions, p => p.AssetType == AssetType.STOCK);
            Assert.Equal(500m, position.DividendsTWD); // 100 shares × 5 TWD
            Assert.Equal(500m, summary.TotalDividendsTWD);
            Assert.Equal(500m, summary.TotalReturnPnlTWD); // no cost basis => UnrealizedPnlTWD is 0
        }

        [Fact]
        public async Task GetSummary_DividendExDateBeforeFirstSnapshot_IsExcluded()
        {
            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });

            // Ex-date is before the user's first snapshot — the app has no way to know this was held.
            _dividendData.Setup(x => x.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(snapshotDate.AddDays(-30), AmountPerUnit: 5m, Currency: "TWD"),
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(0m, summary.TotalDividendsTWD);
        }

        [Fact]
        public async Task GetSummary_QuantityChangedBetweenSnapshots_EachDividendUsesQuantityHeldAtExDate()
        {
            var firstDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40);
            var secondDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20);

            await SeedSnapshotAsync(firstDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 100,
                });
            await SeedSnapshotAsync(secondDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 300, // bought 200 more
                });

            _dividendData.Setup(x => x.GetStockDividendsAsync("2330", "TW", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(firstDate.AddDays(5), AmountPerUnit: 4m, Currency: "TWD"),  // held 100 => 400
                    new(secondDate.AddDays(5), AmountPerUnit: 6m, Currency: "TWD"), // held 300 => 1800
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(2200m, summary.TotalDividendsTWD); // 400 + 1800
        }

        [Fact]
        public async Task GetSummary_UsdStockDividend_ConvertedToTwdAtLiveFx()
        {
            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "AAPL", Name = "Apple",
                    Market = "US", Currency = "USD", Quantity = 10,
                });

            _dividendData.Setup(x => x.GetStockDividendsAsync("AAPL", "US", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(snapshotDate.AddDays(5), AmountPerUnit: 0.26m, Currency: "USD"),
                });

            // FetchUsdTwdRateAsync falls back to 32 when the (unconfigured) HttpClientFactory throws.
            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(32m, summary.UsdTwdRate);
            Assert.Equal(10 * 0.26m * 32m, summary.TotalDividendsTWD);
        }

        [Fact]
        public async Task GetSummary_NoDividendHistory_TotalReturnEqualsUnrealizedPnl()
        {
            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
            await SeedSnapshotAsync(snapshotDate,
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 100_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(0m, summary.TotalDividendsTWD);
            Assert.Equal(summary.UnrealizedPnlTWD, summary.TotalReturnPnlTWD);
            Assert.Equal(100_000m, summary.TotalNetWorthTWD);
        }

        [Fact]
        public async Task GetSummary_RepurchasedAfterSellingOut_OldPeriodDividendNotOnNewPosition()
        {
            // Reported case: fund 043183 was held through early July and paid once, was sold out
            // entirely, then bought back weeks later. On day one of the NEW holding the position
            // already displayed that July payout — income the new holding never produced.
            var heldFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-54);
            var soldOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-39);
            var reboughtOn = DateOnly.FromDateTime(DateTime.UtcNow);

            await SeedSnapshotAsync(heldFrom, Fund(quantity: 3896.46m));
            // Sold out — the fund is simply absent from this snapshot, with cash standing in for it.
            await SeedSnapshotAsync(soldOn,
                new HoldingPosition
                {
                    AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 1_000_000,
                });
            await SeedSnapshotAsync(reboughtOn, Fund(quantity: 1940.37m));

            _dividendData.Setup(x => x.GetFundDividendsAsync("043183", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(heldFrom.AddDays(1), AmountPerUnit: 5m, Currency: "TWD"),
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            var position = Assert.Single(summary.Positions, p => p.Symbol == "043183");
            Assert.Equal(0m, position.DividendsTWD); // was 3896.46 * 5 = 19,482.30

            // It was still genuinely earned, so the lifetime total keeps it — only the attribution
            // to today's position was wrong.
            Assert.Equal(3896.46m * 5m, summary.TotalDividendsTWD);
        }

        [Fact]
        public async Task GetSummary_DividendDuringGapBetweenHoldings_NotCountedAtAll()
        {
            // The complement: a payout with an ex-date inside the sold-out window was never earned,
            // so it must not reach the position OR the lifetime total.
            var heldFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-54);
            var soldOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-39);
            var reboughtOn = DateOnly.FromDateTime(DateTime.UtcNow);

            await SeedSnapshotAsync(heldFrom, Fund(quantity: 3896.46m));
            await SeedSnapshotAsync(soldOn,
                new HoldingPosition
                {
                    AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 1_000_000,
                });
            await SeedSnapshotAsync(reboughtOn, Fund(quantity: 1940.37m));

            _dividendData.Setup(x => x.GetFundDividendsAsync("043183", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(soldOn.AddDays(3), AmountPerUnit: 5m, Currency: "TWD"), // nothing held here
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(0m, Assert.Single(summary.Positions, p => p.Symbol == "043183").DividendsTWD);
            Assert.Equal(0m, summary.TotalDividendsTWD);
        }

        [Fact]
        public async Task GetSummary_HeldContinuously_StillShowsTheWholeHistoryOnThePosition()
        {
            // Guard against over-correcting: an unbroken holding must keep every payout it earned,
            // including ones predating the most recent snapshot.
            var heldFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-54);
            var toppedUpOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-39);

            await SeedSnapshotAsync(heldFrom, Fund(quantity: 1000m));
            await SeedSnapshotAsync(toppedUpOn, Fund(quantity: 2000m));

            _dividendData.Setup(x => x.GetFundDividendsAsync("043183", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>
                {
                    new(heldFrom.AddDays(1), AmountPerUnit: 5m, Currency: "TWD"),    // 1000 held => 5,000
                    new(toppedUpOn.AddDays(1), AmountPerUnit: 5m, Currency: "TWD"),  // 2000 held => 10,000
                });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Equal(15_000m, Assert.Single(summary.Positions, p => p.Symbol == "043183").DividendsTWD);
            Assert.Equal(15_000m, summary.TotalDividendsTWD);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static HoldingPosition Fund(decimal quantity) => new()
        {
            AssetType = AssetType.FUND, Symbol = "043183", Name = "某高股息基金",
            Market = "", Currency = "TWD", Quantity = quantity, UnitPrice = 10m,
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
    }
}
