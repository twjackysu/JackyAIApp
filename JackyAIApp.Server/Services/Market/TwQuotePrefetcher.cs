using JackyAIApp.Server.Common;
using Microsoft.Extensions.Caching.Memory;
using TWStockLib;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Fetches the realtime quotes of many TW stocks at once — TWStockLib's batch, one MIS request per
    /// 50 stocks — into the cache each stock's own quote is read from
    /// (<see cref="CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix"/>, see
    /// <see cref="ITWStockLibHistoricalProvider.GetLatestQuoteAsync"/>). A page valuing every holding then
    /// makes one request or two instead of one per stock, which the library's rate limit (3 per 5s per
    /// host) would queue: ten holdings took about 15s.
    /// </summary>
    public interface ITwQuotePrefetcher
    {
        /// <summary>
        /// Caches the quotes of <paramref name="stockCodes"/> not cached already, during the hours a
        /// today's quote is read (<see cref="TwTodayOverlayMarketDataProvider"/>). Best effort: a stock
        /// with no resolved market, or one the batch did not answer, is quoted on its own as before, and
        /// a failed batch only costs the time it took.
        /// </summary>
        Task PrefetchAsync(IReadOnlyCollection<string> stockCodes, CancellationToken ct);
    }

    public sealed class TwQuotePrefetcher(
        IStockMarketService stocks,
        ITwListedCompanyService listedCompanies,
        IMemoryCache cache,
        TimeProvider time,
        ILogger<TwQuotePrefetcher> logger) : ITwQuotePrefetcher
    {
        public async Task PrefetchAsync(IReadOnlyCollection<string> stockCodes, CancellationToken ct)
        {
            if (!TwTodayOverlayMarketDataProvider.UsesTodayQuote(time)) return;

            var wanted = new List<(string Code, MarketType Market)>();
            foreach (var code in stockCodes.Distinct())
            {
                if (cache.TryGetValue(Key(code), out StockQuote? cached) && cached is not null) continue;
                // The batch asks each stock on one market; an unresolved one keeps the per-stock path,
                // which tries both.
                if (await listedCompanies.GetMarketTypeAsync(code, ct) is { } market)
                    wanted.Add((code, market));
            }

            // One stock is one request either way.
            if (wanted.Count < 2) return;

            try
            {
                var quotes = await stocks.GetRealtimeQuotesAsync(wanted, QuoteOptions.Default, ct);
                foreach (var (code, _) in wanted)
                {
                    if (quotes.TryGetValue(code, out var result) && result.IsSuccess && result.Value is { } quote)
                        cache.Set(Key(code), quote, CacheTtl.Finance.TwRealtimeQuote);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Batch quote of {Count} TW stocks failed; each is quoted on its own", wanted.Count);
            }
        }

        private static string Key(string code) => CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + code;
    }
}
