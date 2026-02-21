namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Factory for resolving market-specific data providers.
    /// </summary>
    public interface IMarketDataProviderFactory
    {
        IMarketDataProvider GetMarketDataProvider(MarketRegion region);
        IFundamentalDataProvider GetFundamentalDataProvider(MarketRegion region);
        IChipDataProvider? GetChipDataProvider(MarketRegion region);

        /// <summary>Detect market region from stock code.</summary>
        MarketRegion DetectRegion(string stockCode);
    }
}
