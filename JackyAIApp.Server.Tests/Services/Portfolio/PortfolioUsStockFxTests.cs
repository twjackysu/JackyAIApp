using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
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
    /// 國泰 複委託 values a US holding as 股數 × 最新價 × 昨收匯率 — its own documented formula. The
    /// rate is therefore the 國泰世華 board rate published for the day the quote belongs to, not
    /// today's and not a mid-market quote.
    ///
    /// The numbers here are a real reconciliation: on 2026-09-04 the broker showed +127 / -81 / -109
    /// against the 2026-09-03 closes, and 國泰世華 published a 31.6800 buy rate for 09-03. A
    /// mid-market rate put the same three positions ~300 TWD out.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioUsStockFxTests : IDisposable
    {
        private const string TestUserId = "us-fx-test-user";

        private static readonly DateOnly PriceDate = new(2026, 9, 3);
        private const decimal CathayBuyRate = 31.6800m;
        private const decimal MidRate = 31.71834m;

        private readonly AzureSQLDBContext _db;
        private readonly Mock<IMarketDataProviderFactory> _providerFactory = new();
        private readonly Mock<IMarketDataProvider> _usProvider = new();
        private readonly Mock<ICathayFxService> _cathayFx = new();

        public PortfolioUsStockFxTests()
        {
            var options = new DbContextOptionsBuilder<AzureSQLDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _db = new AzureSQLDBContext(options);

            _providerFactory.Setup(x => x.DetectRegion(It.IsAny<string>())).Returns(MarketRegion.US);
            _providerFactory.Setup(x => x.GetMarketDataProvider(MarketRegion.US)).Returns(_usProvider.Object);

            SetupPrice("BRK-B", 508.13m);
            SetupPrice("SPMO", 147.41m);
            SetupPrice("NTSD", 48.443m);

            _cathayFx.Setup(x => x.GetBuyRateAsync("USD", PriceDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CathayBuyRate);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task UsStocks_ValuedAtTheBoardRateForTheQuotesOwnDay_MatchTheBroker()
        {
            await SeedAsync(
                Position("BRK-B", 2m, 32_068m),
                Position("SPMO", 2.031010m, 9_566m),
                Position("NTSD", 6m, 9_317m));

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);

            decimal Pnl(string symbol) =>
                Math.Round(Assert.Single(summary.Positions, p => p.Symbol == symbol).UnrealizedPnlTWD!.Value);

            Assert.Equal(127m, Pnl("BRK-B"));
            Assert.Equal(-81m, Pnl("SPMO"));
            Assert.Equal(-109m, Pnl("NTSD"));
        }

        [Fact]
        public async Task UsStock_LooksUpTheRateForThePriceDate_NotToday()
        {
            // The distinction this whole source exists for. Asking for today's rate on an evening
            // when the quote is still yesterday's close drifts by a full session of FX movement.
            await SeedAsync(Position("BRK-B", 2m, 32_068m));

            await CreateService().GetPortfolioSummaryAsync(TestUserId);

            _cathayFx.Verify(x => x.GetBuyRateAsync("USD", PriceDate, It.IsAny<CancellationToken>()), Times.Once);
            _cathayFx.Verify(
                x => x.GetBuyRateAsync(It.IsAny<string>(), It.Is<DateOnly>(d => d != PriceDate), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task UsStock_DisclosesWhichRateProducedTheRow()
        {
            await SeedAsync(Position("BRK-B", 2m, 32_068m));

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions);

            Assert.Equal(CathayBuyRate, position.FxRateUsed);
            Assert.Equal(FxRateSources.Cathay, position.FxRateSource);
        }

        [Fact]
        public async Task UsStock_BoardRateUnavailable_FallsBackToMidMarketRatherThanFailing()
        {
            _cathayFx.Setup(x => x.GetBuyRateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((decimal?)null);
            await SeedAsync(Position("BRK-B", 2m, 32_068m));

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);
            var position = Assert.Single(summary.Positions);

            Assert.Equal("MID", position.FxRateSource);
            Assert.Equal(Math.Round(2m * 508.13m * MidRate - 32_068m),
                Math.Round(position.UnrealizedPnlTWD!.Value));
        }

        [Fact]
        public async Task TwStock_NeverAsksForABoardRate()
        {
            // 國泰世華's rate is the settlement rate for a foreign holding; a TWD holding has none.
            _providerFactory.Setup(x => x.DetectRegion("2330")).Returns(MarketRegion.TW);
            var tw = new Mock<IMarketDataProvider>();
            tw.Setup(x => x.FetchLatestAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData
                {
                    StockCode = "2330",
                    HistoricalPrices = [Bar(1000m)],
                });
            _providerFactory.Setup(x => x.GetMarketDataProvider(MarketRegion.TW)).Returns(tw.Object);

            await SeedAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Name = "台積電",
                Market = "TW", Currency = "TWD", CostCurrency = "TWD", Quantity = 10, CostBasis = 9_000m,
            });

            var summary = await CreateService().GetPortfolioSummaryAsync(TestUserId);

            Assert.Null(Assert.Single(summary.Positions).FxRateUsed);
            _cathayFx.Verify(
                x => x.GetBuyRateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static DailyPrice Bar(decimal close) => new()
        {
            Date = PriceDate.ToDateTime(TimeOnly.MinValue),
            Open = close, High = close, Low = close, Close = close, Volume = 1000,
        };

        private void SetupPrice(string symbol, decimal close) =>
            _usProvider.Setup(x => x.FetchLatestAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = symbol, HistoricalPrices = [Bar(close)] });

        private static HoldingPosition Position(string symbol, decimal quantity, decimal costTWD) => new()
        {
            AssetType = AssetType.STOCK, Symbol = symbol, Name = symbol,
            Market = "US", Currency = "USD", CostCurrency = "TWD",
            Quantity = quantity, CostBasis = costTWD,
        };

        private async Task SeedAsync(params HoldingPosition[] positions)
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

            // Pin the mid-market rate: an unconfigured HttpClientFactory would make
            // FetchUsdTwdRateAsync fall back to 32 and quietly break the arithmetic asserted here.
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("portfolio_usd_twd_rate", MidRate, CacheTtl.Portfolio.Fx);

            return new PortfolioServiceBuilder(_db)
            {
                Providers = _providerFactory.Object,
                Dividends = dividendData.Object,
                Cache = cache,
                CathayFx = _cathayFx.Object,
            }.Build();
        }
    }
}
