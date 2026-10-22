using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Portfolio;
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
    /// A fund bought through 基富通 settles through 集保結算所, which converts at a rate it
    /// negotiates with 華南銀行 — so that, not a mid-market quote, is what the holder actually
    /// receives, and it is the rate 基富通's own app displays. Valuing funds at a mid-market rate
    /// put this app materially away from the figure the user was reconciling against.
    ///
    /// Numbers here are the real reported case: 富達全球優質債券基金 (A股C月配息美元), 5,829.5 units
    /// at NAV 8.071 USD against a 1,500,000 TWD cost.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioFundFxTests : IDisposable
    {
        private const string TestUserId = "fund-fx-test-user";
        private const string FundId = "043183";

        private const decimal Units = 5829.5m;
        private const decimal Nav = 8.071m;
        private const decimal CostTWD = 1_500_000m;

        // 集保 2026/08/27 買入匯率, and the mid-market rate for the same day.
        private const decimal TdccBuyRate = 31.657500m;
        private const decimal MidRate = 31.825974m;

        private readonly AzureSQLDBContext _db;
        private readonly Mock<ITdccFxService> _tdccFx = new();
        private readonly Mock<IFundDataService> _fundData = new();

        public PortfolioFundFxTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(options);

            _fundData.Setup(f => f.GetFundNavHistoryAsync(FundId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory("USD",
                    [new PricePoint { Date = "2026-08-26", Close = Nav }]));
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Fund_ValuedAt集保BuyRate_MatchesWhatTheBrokerShows()
        {
            _tdccFx.Setup(x => x.GetBuyRateAsync("USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TdccBuyRate);
            await SeedFundAsync();

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions, p => p.Symbol == FundId);

            // 5,829.5 x 8.071 x 31.6575 = 1,489,481.xx  ->  P&L -10,518, which is the figure
            // 基富通 displayed. At the mid-market rate this row read -2,591.
            Assert.Equal(-10_518m, Math.Round(position.UnrealizedPnlTWD!.Value));
            Assert.Equal(TdccBuyRate, position.FxRateUsed);
            Assert.Equal(FxRateSources.Tdcc, position.FxRateSource);
        }

        [Fact]
        public async Task UsStock_KeepsMidMarketRate_集保OnlySettlesFunds()
        {
            _tdccFx.Setup(x => x.GetBuyRateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TdccBuyRate);
            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.STOCK, Symbol = "VOO", Name = "Vanguard S&P 500",
                    Market = "US", Currency = "USD", Quantity = 10,
                });

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions, p => p.Symbol == "VOO");

            // A US stock is not settled by 集保, so it must not borrow that rate even when one is
            // loaded for a fund sitting in the same portfolio.
            Assert.NotEqual(FxRateSources.Tdcc, position.FxRateSource);
        }

        [Fact]
        public async Task Fund_集保RateUnavailable_FallsBackToMidMarketRatherThanFailing()
        {
            _tdccFx.Setup(x => x.GetBuyRateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((decimal?)null);
            await SeedFundAsync();

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions, p => p.Symbol == FundId);

            // An FX lookup must never be what stops a portfolio from being valued.
            Assert.Equal("MID", position.FxRateSource);
            Assert.Equal(-2_591m, Math.Round(position.UnrealizedPnlTWD!.Value));
        }

        [Fact]
        public async Task TwdFund_NeedsNoRateAtAll()
        {
            _fundData.Setup(f => f.GetFundNavHistoryAsync("TWDFUND", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory("TWD",
                    [new PricePoint { Date = "2026-08-26", Close = 20m }]));
            await SeedSnapshotAsync(
                new HoldingPosition
                {
                    AssetType = AssetType.FUND, Symbol = "TWDFUND", Name = "某台幣基金",
                    Market = "", Currency = "TWD", Quantity = 1000, CostBasis = 19_000,
                });

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions, p => p.Symbol == "TWDFUND");

            Assert.Null(position.FxRateUsed);
            Assert.Equal(20_000m, position.MarketValueTWD);
            _tdccFx.Verify(
                x => x.GetBuyRateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Task SeedFundAsync() => SeedSnapshotAsync(
            new HoldingPosition
            {
                AssetType = AssetType.FUND, Symbol = FundId,
                Name = "富達基金－全球優質債券基金 (A股C月配息美元)",
                Market = "", Currency = "TWD", CostCurrency = "TWD",
                Quantity = Units, CostBasis = CostTWD,
            });

        private async Task SeedSnapshotAsync(params HoldingPosition[] positions)
        {
            _db.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = TestUserId,
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UsdTwdRate = MidRate,
                Positions = positions.ToList(),
            });
            await _db.SaveChangesAsync();
        }

        private PortfolioService CreateService()
        {
            var dividendData = new Mock<IDividendService>();
            dividendData.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            dividendData.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // Unconfigured HttpClientFactory => FetchUsdTwdRateAsync throws and falls back to 32,
            // which would quietly break the arithmetic these tests assert. Pin the mid rate.
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("portfolio_usd_twd_rate", MidRate, CacheTtl.Portfolio.Fx);

            return new PortfolioServiceBuilder(_db)
            {
                FundData = _fundData.Object,
                Dividends = dividendData.Object,
                Cache = cache,
                TdccFx = _tdccFx.Object,
            }.Build();
        }
    }
}
