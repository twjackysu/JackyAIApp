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
        /// Fetch market data for a specific stock, including enough historical depth for charts/
        /// technical indicators (implementation-defined lookback, currently ~6 months for TW).
        /// </summary>
        Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetch just enough recent data to know the latest price (e.g. for current valuation or a
        /// day-over-day change) — cheap for callers that don't need a full historical series.
        /// Implementations may return more than the minimum if it's already cached/persisted; the
        /// only guarantee is "at least the latest available price", not "at most N days".
        /// </summary>
        Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears any persisted/cached resolution for this symbol that would otherwise make it
        /// stick to a bad value forever (e.g. a wrongly-resolved TW market type) — the next
        /// Fetch*Async call re-derives everything from scratch. Default no-op: most providers have
        /// no sticky per-symbol state to bust, only <c>PersistentTwHistoricalProvider</c> does.
        /// </summary>
        Task InvalidateAsync(string stockCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
