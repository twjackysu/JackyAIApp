using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Keeps Yahoo's request count independent of how many people (or crawlers) load the chart.
    /// One copy serves everyone:
    /// <list type="bullet">
    /// <item><b>Fresh</b> for <see cref="FreshTtl"/> — at most 24 upstream fetches a day.</item>
    /// <item><b>Single flight</b> — concurrent callers on an empty cache share one fetch.</item>
    /// <item><b>Last good</b> kept for <see cref="LastGoodTtl"/>: when a refresh fails, that copy is
    /// served as-is (its <c>AsOf</c> says how old it is). With none, the answer is null.</item>
    /// <item><b>Backoff</b> — after a failed fetch, upstream is left alone for <see cref="FailureBackoff"/>
    /// so an outage costs a handful of requests an hour rather than one per page view.</item>
    /// </list>
    /// The series is never stored in the database: it is six years, and a restart costs one request.
    /// </summary>
    public class CachedMarketIndexHistoryProvider(
        IMarketIndexHistoryProvider inner,
        IMemoryCache cache,
        ILogger<CachedMarketIndexHistoryProvider> logger) : IMarketIndexHistoryProvider
    {
        internal static readonly TimeSpan FreshTtl = TimeSpan.FromHours(1);
        internal static readonly TimeSpan LastGoodTtl = TimeSpan.FromDays(7);
        internal static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(5);

        internal const string FreshKey = "finance:market-index-history:fresh";
        internal const string LastGoodKey = "finance:market-index-history:last-good";
        internal const string BackoffKey = "finance:market-index-history:backoff";

        public async Task<MarketIndexHistoryResponse?> GetAsync(CancellationToken cancellationToken = default)
        {
            if (TryServeFromCache(out var cached)) return cached;

            // The shared fetch must not belong to any one caller: if the first visitor closes the tab,
            // everyone queued behind it still needs the answer. Each caller's token only stops that caller waiting.
            return await SingleFlight.RunAsync(FreshKey, RefreshAsync, cancellationToken);
        }

        /// <summary>True when no fetch is needed: a fresh copy, or a recent failure to ride out.</summary>
        private bool TryServeFromCache(out MarketIndexHistoryResponse? history)
        {
            if (cache.TryGetValue(FreshKey, out MarketIndexHistoryResponse? fresh))
            {
                history = fresh;
                return true;
            }

            if (cache.TryGetValue(BackoffKey, out _))
            {
                history = cache.TryGetValue(LastGoodKey, out MarketIndexHistoryResponse? lastGood) ? lastGood : null;
                return true;
            }

            history = null;
            return false;
        }

        private async Task<MarketIndexHistoryResponse?> RefreshAsync()
        {
            // Another flight may have finished between the caller's miss and this one starting.
            if (TryServeFromCache(out var cached)) return cached;

            MarketIndexHistoryResponse? fetched = null;
            try
            {
                fetched = await inner.GetAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Market index history provider threw");
            }

            if (fetched is not null)
            {
                cache.Set(FreshKey, fetched, FreshTtl);
                cache.Set(LastGoodKey, fetched, LastGoodTtl);
                return fetched;
            }

            cache.Set(BackoffKey, true, FailureBackoff);
            if (cache.TryGetValue(LastGoodKey, out MarketIndexHistoryResponse? lastGood))
            {
                logger.LogWarning("Market index history refresh failed; serving last good copy as of {asOf}", lastGood!.AsOf);
                return lastGood;
            }

            logger.LogError("Market index history refresh failed and there is no earlier copy to fall back on");
            return null;
        }
    }
}
