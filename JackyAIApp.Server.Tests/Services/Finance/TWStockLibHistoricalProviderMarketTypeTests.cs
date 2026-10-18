using JackyAIApp.Server.Services.ExternalApi;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Portfolio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Tests.Services.Finance
{
    /// <summary>
    /// TSE/OTC resolution comes from the stored listed-company list, not TWStockLib's
    /// GetAllStockListAsync (a scrape of isin.twse.com.tw that timed out at 30s). The realtime
    /// overlay resolves the market before every TW quote from 09:00 on, so while it came from the
    /// scrape, every TW price fetch — and the portfolio summary — waited out that timeout.
    /// </summary>
    public class TWStockLibHistoricalProviderMarketTypeTests
    {
        private readonly Mock<IStockMarketService> _stockService = new();
        private readonly Mock<ITwListedCompanyService> _listedCompanies = new();
        private readonly TWStockLibHistoricalProvider _provider;

        public TWStockLibHistoricalProviderMarketTypeTests()
        {
            _provider = new TWStockLibHistoricalProvider(
                _stockService.Object,
                _listedCompanies.Object,
                new MemoryCache(new MemoryCacheOptions()),
                Mock.Of<IThirdPartyGateway>(),
                NullLogger<TWStockLibHistoricalProvider>.Instance);
        }

        [Theory]
        [InlineData(MarketType.TSE)]
        [InlineData(MarketType.OTC)]
        public async Task DetermineMarketType_ComesFromTheListedCompanyList(MarketType market)
        {
            _listedCompanies.Setup(x => x.GetMarketTypeAsync("6488", It.IsAny<CancellationToken>())).ReturnsAsync(market);

            Assert.Equal(market, await _provider.DetermineMarketType("6488"));
            _stockService.Verify(x => x.GetAllStockListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DetermineMarketType_NotInTheList_IsUnresolvedRatherThanGuessed()
        {
            _listedCompanies.Setup(x => x.GetMarketTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MarketType?)null);

            Assert.Null(await _provider.DetermineMarketType("9999"));
            _stockService.Verify(x => x.GetAllStockListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DetermineMarketType_ListLookupThrows_IsUnresolved()
        {
            _listedCompanies.Setup(x => x.GetMarketTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database unavailable"));

            Assert.Null(await _provider.DetermineMarketType("2330"));
        }

        [Fact]
        public async Task GetLatestQuoteAsync_UnknownMarket_AsksTheListedMarketFirst()
        {
            // The overlay's call: no market supplied. An OTC symbol must go straight to the OTC
            // quote instead of a failed TSE attempt first.
            _listedCompanies.Setup(x => x.GetMarketTypeAsync("6488", It.IsAny<CancellationToken>())).ReturnsAsync(MarketType.OTC);
            var quote = new StockQuote { Symbol = "6488", Name = "環球晶", Market = MarketType.OTC };
            _stockService.Setup(x => x.GetRealtimeQuoteAsync("6488", MarketType.OTC, It.IsAny<CancellationToken>()))
                .ReturnsAsync(StockResult<StockQuote>.Ok(quote));

            Assert.Same(quote, await _provider.GetLatestQuoteAsync("6488", knownMarketType: null));
            _stockService.Verify(x => x.GetRealtimeQuoteAsync("6488", MarketType.TSE, It.IsAny<CancellationToken>()), Times.Never);
            _stockService.Verify(x => x.GetAllStockListAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
