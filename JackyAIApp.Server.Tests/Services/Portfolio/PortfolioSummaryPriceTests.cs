using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Portfolio;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Pins which price the summary values each holding at, and when it says the price could not be
    /// had: the latest close for a stock; for a fund, its latest NAV, else the unit price saved with it.
    /// </summary>
    [Collection(SingleFlightCollection.Name)]
    public class PortfolioSummaryPriceTests : IDisposable
    {
        private const string UserId = "summary-price-user";

        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Mock<IFundDataService> _fundData = new();
        private readonly Mock<IMarketDataProviderFactory> _providers = new();
        private readonly Mock<IMarketDataProvider> _provider = new();

        public PortfolioSummaryPriceTests()
        {
            _providers.Setup(x => x.DetectRegion(It.IsAny<string>())).Returns(MarketRegion.TW);
            _providers.Setup(x => x.GetMarketDataProvider(MarketRegion.TW)).Returns(_provider.Object);
        }

        public void Dispose() => _db.Dispose();

        private async Task<PositionDto> SummaryOfAsync(HoldingPosition position)
        {
            _db.HoldingSnapshots.Add(new HoldingSnapshot
            {
                UserId = UserId,
                EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
                UsdTwdRate = 32m,
                Positions = [position],
            });
            await _db.SaveChangesAsync();

            var dividends = new Mock<IDividendService>();
            dividends.Setup(x => x.GetStockDividendsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            dividends.Setup(x => x.GetFundDividendsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set("portfolio_usd_twd_rate", 30m, CacheTtl.Portfolio.Fx);

            var summary = await new PortfolioServiceBuilder(_db)
            {
                Providers = _providers.Object, FundData = _fundData.Object, Dividends = dividends.Object, Cache = cache,
            }.Build().GetPortfolioSummaryAsync(UserId);
            return Assert.Single(summary.Positions);
        }

        private static HoldingPosition Fund(string symbol, decimal? unitPrice, string currency = "TWD") => new()
        {
            AssetType = AssetType.FUND, Symbol = symbol, Name = "f", Currency = currency, Quantity = 10, UnitPrice = unitPrice,
        };

        private void Nav(string fundId, string currency, decimal close) =>
            _fundData.Setup(f => f.GetFundNavHistoryAsync(fundId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundNavHistory(currency, [new PricePoint { Date = "2026-10-01", Close = close }]));

        private void Bars(string symbol, decimal close) =>
            _provider.Setup(x => x.FetchLatestAsync(symbol, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { StockCode = symbol, HistoricalPrices = [new DailyPrice { Date = DateTime.UtcNow.Date, Close = close }] });

        [Fact]
        public async Task Fund_IsValuedAtItsLatestNav_InTheNavsCurrency()
        {
            Nav("F1", "USD", 8m);

            var p = await SummaryOfAsync(Fund("F1", unitPrice: 5m, currency: "TWD"));

            Assert.Equal(8m, p.CurrentPrice);
            Assert.Equal("USD", p.Currency);
            Assert.Equal(8m * 10 * 30m, p.MarketValueTWD);
            Assert.False(p.PriceFetchFailed);
        }

        [Fact]
        public async Task Fund_WithoutNav_IsValuedAtItsSavedUnitPrice_InItsOwnCurrency()
        {
            var p = await SummaryOfAsync(Fund("F1", unitPrice: 5m));

            Assert.Equal(5m, p.CurrentPrice);
            Assert.Equal("TWD", p.Currency);
            Assert.False(p.PriceFetchFailed);
        }

        [Fact]
        public async Task Fund_WhoseLatestNavIsZero_FallsBackToTheUnitPrice_ButStaysInTheNavsCurrency()
        {
            Nav("F1", "USD", 0m);

            var p = await SummaryOfAsync(Fund("F1", unitPrice: 5m));

            Assert.Equal(5m, p.CurrentPrice);
            Assert.Equal("USD", p.Currency);
            Assert.False(p.PriceFetchFailed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Fund_WithNeitherNavNorUnitPrice_HasNoPrice(bool zeroNav)
        {
            if (zeroNav) Nav("F1", "TWD", 0m);

            var p = await SummaryOfAsync(Fund("F1", unitPrice: null));

            Assert.True(p.PriceFetchFailed);
            Assert.Equal(0m, p.MarketValueTWD);
        }

        [Fact]
        public async Task Stock_IsValuedAtItsLatestClose()
        {
            Bars("2330", 1000m);

            var p = await SummaryOfAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Market = "TW", Currency = "TWD", Quantity = 2,
            });

            Assert.Equal(1000m, p.CurrentPrice);
            Assert.Equal(2000m, p.MarketValueTWD);
            Assert.False(p.PriceFetchFailed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stock_WithNoPricesOrAZeroClose_HasNoPrice(bool zeroClose)
        {
            if (zeroClose) Bars("2330", 0m);
            else _provider.Setup(x => x.FetchLatestAsync("2330", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());

            var p = await SummaryOfAsync(new HoldingPosition
            {
                AssetType = AssetType.STOCK, Symbol = "2330", Market = "TW", Currency = "TWD", Quantity = 2,
            });

            Assert.True(p.PriceFetchFailed);
        }
    }
}
