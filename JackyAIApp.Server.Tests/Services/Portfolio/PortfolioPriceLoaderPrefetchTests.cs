using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>
    /// Fetching many stocks first batches the quotes of the TW ones about to be fetched (see
    /// <see cref="ITwQuotePrefetcher"/>), so their fetches find their quotes cached.
    /// </summary>
    public class PortfolioPriceLoaderPrefetchTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly Mock<IMarketDataProviderFactory> _providers = new();
        private readonly Mock<IMarketDataProvider> _provider = new();
        private readonly Mock<ITwQuotePrefetcher> _twQuotes = new();
        private readonly List<string> _events = [];

        public PortfolioPriceLoaderPrefetchTests()
        {
            _providers.Setup(p => p.DetectRegion(It.IsAny<string>()))
                .Returns((string s) => char.IsDigit(s[0]) ? MarketRegion.TW : MarketRegion.US);
            _providers.Setup(p => p.GetMarketDataProvider(It.IsAny<MarketRegion>())).Returns(_provider.Object);
            _provider.Setup(p => p.FetchLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string s, CancellationToken _) =>
                {
                    lock (_events) _events.Add("fetch " + s);
                    return new MarketData
                    {
                        StockCode = s,
                        HistoricalPrices = [new DailyPrice { Date = new DateTime(2026, 10, 21), Close = 100m }],
                    };
                });
            _twQuotes.Setup(p => p.PrefetchAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyCollection<string> codes, CancellationToken _) =>
                {
                    lock (_events) _events.Add("prefetch " + string.Join(",", codes.Order()));
                })
                .Returns(Task.CompletedTask);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task FetchStocks_BatchesTheQuotesOfTheTwStocksItIsAboutToFetch_First()
        {
            var loader = new PortfolioServiceBuilder(_db) { Providers = _providers.Object, TwQuotes = _twQuotes.Object }
                .BuildPriceLoader();
            // 6488 is cached already: its fetch will not ask for a quote.
            await loader.FetchStockAsync("6488", deep: false, CancellationToken.None);
            _events.Clear();

            var prices = await loader.FetchStocksAsync(["2330", "0050", "6488", "AAPL"], deep: false, CancellationToken.None);

            Assert.Equal(4, prices.Count);
            Assert.Equal("prefetch 0050,2330", _events[0]);
            Assert.Equal(new[] { "fetch 0050", "fetch 2330", "fetch AAPL" }, _events.Skip(1).Order());
        }
    }
}
