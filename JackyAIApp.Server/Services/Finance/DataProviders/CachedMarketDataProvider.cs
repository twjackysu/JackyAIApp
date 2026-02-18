using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Decorator that adds caching to any IMarketDataProvider.
    /// Cache key is based on provider type, stock code, and current date.
    /// </summary>
    public class CachedMarketDataProvider : IMarketDataProvider
    {
        private readonly IMarketDataProvider _inner;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachedMarketDataProvider> _logger;
        private readonly TimeSpan _cacheDuration;

        public DataProviderType Type => _inner.Type;

        public CachedMarketDataProvider(
            IMarketDataProvider inner,
            IMemoryCache cache,
            ILogger<CachedMarketDataProvider> logger,
            TimeSpan? cacheDuration = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheDuration = cacheDuration ?? TimeSpan.FromHours(4);
        }

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"MarketData_{_inner.Type}_{stockCode}_{DateTime.Today:yyyyMMdd}";

            if (_cache.TryGetValue(cacheKey, out MarketData? cachedData) && cachedData != null)
            {
                _logger.LogDebug("Cache hit for {type} data of {stockCode}", _inner.Type, stockCode);
                return cachedData;
            }

            _logger.LogDebug("Cache miss for {type} data of {stockCode}, fetching...", _inner.Type, stockCode);
            var data = await _inner.FetchAsync(stockCode, cancellationToken);

            // Only cache if we got meaningful data
            if (data.HistoricalPrices.Count > 0 || data.Fundamentals != null || data.Chips != null)
            {
                _cache.Set(cacheKey, data, _cacheDuration);
            }

            return data;
        }
    }
}
