using JackyAIApp.Server.Services.Market.US;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Routes to the correct data providers based on market region.
    /// TW stock codes are 4-6 digit numbers. US stock codes are alphabetic.
    /// </summary>
    public class MarketDataProviderFactory : IMarketDataProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public MarketDataProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public MarketRegion DetectRegion(string stockCode)
        {
            if (string.IsNullOrWhiteSpace(stockCode)) return MarketRegion.TW;
            return TwStockCode.TryNormalize(stockCode, out _) ? MarketRegion.TW : MarketRegion.US;
        }

        public IMarketDataProvider GetMarketDataProvider(MarketRegion region)
        {
            return region switch
            {
                MarketRegion.US => _serviceProvider.GetRequiredService<YahooFinanceMarketDataProvider>(),
                _ => _serviceProvider.GetRequiredService<IMarketDataProvider>(),
            };
        }

        public IFundamentalDataProvider GetFundamentalDataProvider(MarketRegion region)
        {
            return region switch
            {
                MarketRegion.US => _serviceProvider.GetRequiredService<SECFundamentalDataProvider>(),
                _ => _serviceProvider.GetRequiredService<IFundamentalDataProvider>(),
            };
        }

        public IChipDataProvider? GetChipDataProvider(MarketRegion region)
        {
            return region switch
            {
                MarketRegion.US => null, // US market has no chip (籌碼) data equivalent
                _ => _serviceProvider.GetRequiredService<IChipDataProvider>(),
            };
        }
    }
}
