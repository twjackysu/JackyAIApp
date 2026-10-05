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
    /// The day change compares today's liquid net worth with the previous trading day's. Both sides
    /// must convert foreign holdings at the same rate, or the gap between two rates is reported as
    /// if it were a gain or loss.
    ///
    /// They did not: today's side used 國泰's rate for US stocks (and 集保's for funds), while the
    /// previous day's side used the mid-market rate stored on the snapshot when it was saved —
    /// possibly weeks earlier. With no trade and no price move, US$30k of stock read as a
    /// NT$15,000 (-1.42%) loss every day.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioDayChangeFxTests : IDisposable
    {
        private const string UserId = "day-change-fx-user";
        private static readonly DateOnly Monday = new(2026, 10, 5);
        private static readonly DateOnly Friday = new(2026, 10, 2);

        private const decimal CathayRate = 31.5m;     // today's 國泰 rate, for any quote date
        private const decimal MidRate = 31.6m;        // today's mid-market rate
        private const decimal StaleSnapshotRate = 32m; // mid-market rate stored when the snapshot was saved

        private readonly AzureSQLDBContext _db;
        private readonly Mock<IMarketDataProviderFactory> _providers = new();
        private readonly PortfolioService _service;

        public PortfolioDayChangeFxTests()
        {
            _db = new AzureSQLDBContext(new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var dividends = new Mock<IDividendService>();
            dividends.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            dividends.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var cathay = new Mock<ICathayFxService>();
            cathay.Setup(x => x.GetBuyRateAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CathayRate);

            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("portfolio_usd_twd_rate", MidRate, TimeSpan.FromHours(1));

            _service = new PortfolioService(
                _db, _providers.Object, new Mock<IFundDataService>().Object, dividends.Object,
                new Mock<IStockCategoryService>().Object, stockService: null!, new Mock<IHttpClientFactory>().Object,
                cache, new Mock<IOpenAIPromptService>().Object, new Mock<IUsageLimitService>().Object,
                NullLogger<PortfolioService>.Instance,
                timeProvider: new FixedTime(Monday),
                cathayFx: cathay.Object);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task UsStockUnchanged_PriceFlat_NoChangeFromAStaleSnapshotRate()
        {
            // Nothing traded and the price didn't move, so there is nothing to report — however
            // long ago the snapshot (and the rate stored with it) was saved.
            SetupPrices("NTSD", friClose: 30m, monClose: 30m);
            await SeedAsync(new DateOnly(2026, 9, 1), UsStock("NTSD", 1000), TwdCash(100_000));

            var summary = await _service.GetPortfolioSummaryAsync(UserId);

            Assert.Equal(0m, summary.DayChangeTWD);
            Assert.Equal(0m, summary.DayChangePct);
        }

        [Fact]
        public async Task UsStockPriceMove_IsValuedAtOneRateOnBothSides()
        {
            // 1000 sh moving US$30 -> US$31 at 國泰's 31.5 is NT$31,500: the price move only.
            SetupPrices("NTSD", friClose: 30m, monClose: 31m);
            await SeedAsync(new DateOnly(2026, 9, 1), UsStock("NTSD", 1000));

            var summary = await _service.GetPortfolioSummaryAsync(UserId);

            Assert.Equal(31_500m, summary.DayChangeTWD);
        }

        [Fact]
        public async Task UsStockSoldIntoTwdCash_SoldSideValuedAtTheSameCathayRate()
        {
            // The sold stock exists only on Friday's side. It still has to be valued at 國泰's rate
            // there: at the mid-market fallback (31.6) Friday would be worth NT$3,000 more, and the
            // realized gain would read NT$3,000 smaller than it was.
            SetupPrices("NTSD", friClose: 30m, monClose: 31m);
            await SeedAsync(Friday, UsStock("NTSD", 1000));
            // Sold at US$31 and settled into TWD at 國泰's rate: 1000 x 31 x 31.5.
            await SeedAsync(Monday, TwdCash(976_500));

            var summary = await _service.GetPortfolioSummaryAsync(UserId);

            // 976,500 - 1000 x 30 x 31.5 (945,000)
            Assert.Equal(31_500m, summary.DayChangeTWD);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static HoldingPosition UsStock(string symbol, decimal quantity) => new()
        {
            AssetType = AssetType.STOCK, Symbol = symbol, Name = symbol,
            Market = "US", Currency = "USD", Quantity = quantity,
        };

        private static HoldingPosition TwdCash(decimal amount) => new()
        {
            AssetType = AssetType.CASH, Name = "現金", Currency = "TWD", Quantity = amount,
        };

        private async Task SeedAsync(DateOnly effectiveDate, params HoldingPosition[] positions)
        {
            _db.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = UserId,
                EffectiveDate = effectiveDate,
                UsdTwdRate = StaleSnapshotRate,
                Positions = positions.ToList(),
            });
            await _db.SaveChangesAsync();
        }

        private void SetupPrices(string symbol, decimal friClose, decimal monClose)
        {
            var data = new MarketData
            {
                StockCode = symbol,
                HistoricalPrices =
                [
                    new DailyPrice { Date = Friday.ToDateTime(TimeOnly.MinValue), Close = friClose },
                    new DailyPrice { Date = Monday.ToDateTime(TimeOnly.MinValue), Close = monClose },
                ],
            };
            var provider = new Mock<IMarketDataProvider>();
            provider.Setup(x => x.FetchAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            provider.Setup(x => x.FetchLatestAsync(symbol, It.IsAny<CancellationToken>())).ReturnsAsync(data);
            _providers.Setup(x => x.DetectRegion(symbol)).Returns(MarketRegion.US);
            _providers.Setup(x => x.GetMarketDataProvider(MarketRegion.US)).Returns(provider.Object);
        }

        private sealed class FixedTime(DateOnly today) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero);
        }
    }
}
