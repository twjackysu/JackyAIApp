using JackyAIApp.Server.Services.Finance.DataProviders.US;
using System.Text.RegularExpressions;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Routes to the correct data providers based on market region.
    /// TW stock codes are 4-6 digit numbers. US stock codes are alphabetic.
    /// </summary>
    public class MarketDataProviderFactory : IMarketDataProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        // TW stock codes: 4-6 digits, optionally ending with a letter (e.g., "2330", "6547", "00878")
        private static readonly Regex TwStockPattern = new(@"^\d{4,6}[A-Za-z]?$", RegexOptions.Compiled);

        public MarketDataProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public MarketRegion DetectRegion(string stockCode)
        {
            if (string.IsNullOrWhiteSpace(stockCode)) return MarketRegion.TW;
            return TwStockPattern.IsMatch(stockCode.Trim()) ? MarketRegion.TW : MarketRegion.US;
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
