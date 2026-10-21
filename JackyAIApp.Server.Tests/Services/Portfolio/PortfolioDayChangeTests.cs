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
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioDayChangeTests : IDisposable
    {
        private const string TestUserId = "day-change-test-user";
        private static readonly DateOnly Friday = new(2026, 10, 2);
        private static readonly DateOnly Monday = new(2026, 10, 5);

        private readonly AzureSQLDBContext _dbContext;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly PortfolioService _service;

        public PortfolioDayChangeTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            _dbContext = new AzureSQLDBContext(options);

            var fundData = new Mock<IFundDataService>();
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DividendRecord>());
            var httpClientFactory = new Mock<IHttpClientFactory>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var openAI = new Mock<IOpenAIPromptService>();
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
                NullLogger<PortfolioService>.Instance,
                timeProvider: new FixedTime(Monday));
        }

        public void Dispose() => _dbContext.Dispose();

        [Fact]
        public async Task GetSummary_StockSoldToCash_ReflectsDayChangeFromPreviousTradingDay()
        {
            // Friday: held 10 shares 2330 (close 1000) + 10,000 cash = 20,000 liquid net assets
            // Monday: sold 2330 at 1050 -> 0 shares, 20,500 cash = 20,500 liquid net assets
            SetupStockPrices("2330", (Friday, 1000m), (Monday, 1050m));

            await SeedSnapshotAsync(Friday,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 10,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 10_000 });

            await SeedSnapshotAsync(Monday,
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 20_500 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Day change is 20,500 - 20,000 = +500 (+2.50%)
            Assert.Equal(500m, summary.DayChangeTWD);
            Assert.Equal(2.50m, summary.DayChangePct);
        }

        [Fact]
        public async Task GetSummary_HoldingConstant_ReflectsStockPriceMove()
        {
            // Friday: held 10 shares 2330 (close 1000) + 10,000 cash = 20,000 liquid net assets
            // Monday: held 10 shares 2330 (close 1050) + 10,000 cash = 20,500 liquid net assets
            SetupStockPrices("2330", (Friday, 1000m), (Monday, 1050m));

            await SeedSnapshotAsync(Friday,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 10,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 10_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // 20,500 - 20,000 = +500 (+2.50%)
            Assert.Equal(500m, summary.DayChangeTWD);
            Assert.Equal(2.50m, summary.DayChangePct);
        }

        [Fact]
        public async Task GetSummary_RealEstateExcludedFromLiquidDayChange()
        {
            // Real estate (20,000,000) does not distort liquid net assets day change amount or percentage
            SetupStockPrices("2330", (Friday, 1000m), (Monday, 1050m));

            await SeedSnapshotAsync(Friday,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 10,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 10_000 },
                new HoldingPosition { AssetType = AssetType.REAL_ESTATE, Name = "房產", Currency = "TWD", Quantity = 20_000_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Real estate is excluded from liquid net worth: denominator is 20,000, not 20,020,000
            Assert.Equal(500m, summary.DayChangeTWD);
            Assert.Equal(2.50m, summary.DayChangePct);
        }

        [Fact]
        public async Task GetSummary_LiabilitySubtractedInLiquidNetWorthDayChange()
        {
            // Friday: 10 * 1000 (stock) + 10,000 (cash) - 4,000 (debt) = 16,000 liquid net assets
            // Monday: 10 * 1050 (stock) + 10,000 (cash) - 4,000 (debt) = 16,500 liquid net assets
            SetupStockPrices("2330", (Friday, 1000m), (Monday, 1050m));

            await SeedSnapshotAsync(Friday,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 10,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 10_000 },
                new HoldingPosition { AssetType = AssetType.LIABILITY, Name = "信貸", Currency = "TWD", Quantity = 4_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            // Change = +500 (debt counts against the amount). The % is of Friday's gross liquid
            // assets, 20,000, not net assets: the same basis as the unrealized and total-return
            // percentages beside it. Net assets (16,000) would read 3.12% and overstate the move.
            Assert.Equal(500m, summary.DayChangeTWD);
            Assert.Equal(2.50m, summary.DayChangePct);
        }

        [Fact]
        public async Task GetSummary_NoNewerPriceAndNoHoldingChange_ReturnsNullDayChange()
        {
            // Only Friday price exists (e.g. Monday morning before market opens). Holdings haven't changed.
            SetupStockPrices("2330", (Friday, 1000m));

            await SeedSnapshotAsync(Friday,
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                    Market = "TW", Currency = "TWD", Quantity = 10,
                },
                new HoldingPosition { AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = 10_000 });

            var summary = await _service.GetPortfolioSummaryAsync(TestUserId);

            Assert.Null(summary.DayChangeTWD);
            Assert.Null(summary.DayChangePct);
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

        private void SetupStockPrices(string symbol, params (DateOnly Date, decimal Price)[] history)
        {
            var provider = new Mock<IMarketDataProvider>();
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices = history.Select(h => new DailyPrice
                {
                    Date = h.Date.ToDateTime(TimeOnly.MinValue),
                    Close = h.Price,
                }).ToList(),
            };
            provider.Setup(p => p.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            provider.Setup(p => p.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            _providerFactory.Setup(f => f.DetectRegion(symbol)).Returns(MarketRegion.TW);
            _providerFactory.Setup(f => f.GetMarketDataProvider(MarketRegion.TW)).Returns(provider.Object);
        }

        private sealed class FixedTime(DateOnly today) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero);
        }
    }
}
