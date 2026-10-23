using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Decorator that adds caching to any IMarketDataProvider.
    /// Cache key is based on provider type, stock code, and the date in Taipei.
    /// </summary>
    public class CachedMarketDataProvider : IMarketDataProvider
    {
        private readonly IMarketDataProvider _inner;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachedMarketDataProvider> _logger;
        private readonly TimeSpan _cacheDuration;
        private readonly TimeProvider _time;

        public DataProviderType Type => _inner.Type;

        public CachedMarketDataProvider(
            IMarketDataProvider inner,
            IMemoryCache cache,
            ILogger<CachedMarketDataProvider> logger,
            TimeSpan? cacheDuration = null,
            TimeProvider? timeProvider = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheDuration = cacheDuration ?? TimeSpan.FromHours(4);
            _time = timeProvider ?? TimeProvider.System;
        }

        public Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchInternalAsync(stockCode, deep: true, cancellationToken);

        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchInternalAsync(stockCode, deep: false, cancellationToken);

        /// <summary>
        /// Must be overridden, not left as the interface's default no-op: this decorator sits in
        /// front of every <see cref="IMarketDataProvider"/> registration (see
        /// FinanceServiceExtensions), so a caller invoking <c>InvalidateAsync</c> through here would
        /// otherwise silently no-op and never reach <c>_inner</c>'s real invalidation logic (e.g.
        /// <c>PersistentTwHistoricalProvider</c> resetting a stuck DB-persisted market-type
        /// resolution) — exactly the bug that made Portfolio's "重新抓取報價" reload button do
        /// nothing for 00864B.
        /// </summary>
        public async Task InvalidateAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            _cache.Remove(CacheKey(stockCode, deep: true));
            _cache.Remove(CacheKey(stockCode, deep: false));
            await _inner.InvalidateAsync(stockCode, cancellationToken);
        }

        /// <summary>Today's entry, Taipei's today: a new trading day's prices are a new entry from midnight there.</summary>
        private string CacheKey(string stockCode, bool deep) =>
            $"MarketData_{_inner.Type}_{(deep ? "deep" : "latest")}_{stockCode}_{_time.GetTaipeiToday():yyyyMMdd}";

        private async Task<MarketData> FetchInternalAsync(string stockCode, bool deep, CancellationToken cancellationToken)
        {
            // Separate cache keys for deep vs latest-only — a shallow FetchLatestAsync result must
            // never be served back to a caller that asked for the full (deep) series, and vice
            // versa serving deep data to a latest-only caller is harmless but wasteful to key together.
            var cacheKey = CacheKey(stockCode, deep);

            // One fetch for a burst of callers on a cold key (a page asks for the same stock from
            // several panels at once). Shared, so it runs without any one caller's token.
            return await _cache.GetOrCreateSingleFlightAsync(cacheKey, _cacheDuration, () =>
                {
                    _logger.LogDebug("Cache miss for {type} data of {stockCode}, fetching...", _inner.Type, stockCode);
                    return deep
                        ? _inner.FetchAsync(stockCode, CancellationToken.None)
                        : _inner.FetchLatestAsync(stockCode, CancellationToken.None);
                },
                cancellationToken,
                // Only cache if we got meaningful data
                shouldCache: data => data.HistoricalPrices.Count > 0 || data.Fundamentals != null || data.Chips != null);
        }
    }
}
