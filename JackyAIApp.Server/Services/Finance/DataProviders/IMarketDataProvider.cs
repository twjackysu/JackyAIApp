using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Strategy interface for fetching market data from various sources.
    /// Implementations can be swapped or decorated (e.g., with caching) independently.
    /// </summary>
    public interface IMarketDataProvider
    {
        /// <summary>The type of data this provider fetches</summary>
        DataProviderType Type { get; }

        /// <summary>
        /// Fetch market data for a specific stock.
        /// </summary>
        Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Types of market data providers.
    /// </summary>
    public enum DataProviderType
    {
        /// <summary>Historical OHLCV price data</summary>
        HistoricalPrice,

        /// <summary>Fundamental financial data (P/E, revenue, etc.)</summary>
        Fundamental,

        /// <summary>Chip analysis data (margin, foreign holding, etc.)</summary>
        Chip
    }
}
