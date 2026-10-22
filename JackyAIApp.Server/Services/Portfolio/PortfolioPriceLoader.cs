using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// The prices every Portfolio screen reads — stocks, fund NAVs, the live USD/TWD rate — each
    /// cached and single-flighted, so the endpoints of one page load share a fetch.
    /// </summary>
    public interface IPortfolioPriceLoader
    {
        /// <summary>
        /// One stock's market data, or null on failure. <paramref name="deep"/> = false is for callers
        /// that only read the latest price (summary valuation, day change, lookup): a symbol not yet
        /// cached or stored then costs a shallow fetch (~10 days for TW) instead of the full ~6-month
        /// window. Pass true for anything that reads more than the last price (charts, the AI summary).
        /// </summary>
        Task<MarketData?> FetchStockAsync(string symbol, bool deep, CancellationToken ct);

        /// <summary>Every distinct symbol in parallel; failed symbols are left out.</summary>
        Task<Dictionary<string, MarketData>> FetchStocksAsync(IEnumerable<string> symbols, bool deep, CancellationToken ct);

        /// <summary>Every distinct fund's NAV history in parallel (FundDataService caches each); failed ones are left out.</summary>
        Task<Dictionary<string, FundNavHistory>> FetchFundsAsync(IEnumerable<string> fundIds, CancellationToken ct);

        /// <summary>The live USD/TWD rate, or <see cref="PortfolioPriceLoader.FallbackUsdTwdRate"/> when the feed fails.</summary>
        Task<decimal> FetchUsdTwdRateAsync(CancellationToken ct);

        /// <summary>Drops the stock's cached prices, here and in its provider, so the next fetch goes to the source.</summary>
        Task InvalidateStockAsync(string symbol, CancellationToken ct);
    }

    public class PortfolioPriceLoader(
        IMarketDataProviderFactory providerFactory,
        IFundDataService fundData,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<PortfolioPriceLoader> logger) : IPortfolioPriceLoader
    {
        /// <summary>USD/TWD used when no real rate is available: the live feed failed, or a snapshot was
        /// saved without one (0). One number for both, so a fallback never differs between screens.</summary>
        public const decimal FallbackUsdTwdRate = 32m;

        private const string FX_URL = "https://open.er-api.com/v6/latest/USD";
        private const string FX_CACHE_KEY = "portfolio_usd_twd_rate";
        private const string STOCK_CACHE_PREFIX = "portfolio_stock_";

        public async Task<MarketData?> FetchStockAsync(string symbol, bool deep, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            // Separate cache keys for deep vs latest-only — a shallow result must never be served
            // back to a caller that needs the full series (see PersistentTwHistoricalProvider's own
            // matching split for why: it's not just a perf nicety, a stale shallow cache hit would
            // silently starve a chart/indicator caller of history it thinks it has).
            var key = STOCK_CACHE_PREFIX + (deep ? "deep_" : "latest_") + symbol;
            if (cache.TryGetValue(key, out MarketData? cached) && cached != null)
                return cached;

            // A shallow caller only ever reads the last point (see FetchStockAsync's doc comment),
            // and deep ⊇ latest, so if a deep fetch already landed for this symbol — likely from a
            // concurrent chart/history request for the very same page load — reuse it instead of a
            // second upstream hit. Not written back under the latest_ key: that cache must only ever
            // hold a genuine shallow result (see the comment above).
            if (!deep && cache.TryGetValue(STOCK_CACHE_PREFIX + "deep_" + symbol, out MarketData? deepCached) && deepCached != null)
                return deepCached;

            // Single-flight: on a cold cache, the summary/net-worth/benchmark/drawdown endpoints for
            // the same symbol land here concurrently (same page load). Only the first caller actually
            // fetches; the rest await that one task. The underlying fetch runs with its own
            // CancellationToken.None (see SingleFlight's doc) so one caller cancelling never cancels
            // the fetch the others are still waiting on.
            return await cache.GetOrCreateSingleFlightAsync(key, CacheTtl.Portfolio.StockPrice, async () =>
            {
                try
                {
                    var region = providerFactory.DetectRegion(symbol);
                    var provider = providerFactory.GetMarketDataProvider(region);
                    // TW's provider chain already includes today's-price overlay as its outermost
                    // layer (TwTodayOverlayMarketDataProvider, see FinanceServiceExtensions) — every
                    // caller of IMarketDataProvider gets it, not just Portfolio, so nothing extra
                    // happens here.
                    return deep
                        ? await provider.FetchAsync(symbol, CancellationToken.None)
                        : await provider.FetchLatestAsync(symbol, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch price for {Symbol}", symbol);
                    return null;
                }
            }, ct,
                // Only cache a usable result; an empty fetch shouldn't suppress the next retry.
                shouldCache: data => data?.HistoricalPrices.Count > 0);
        }

        public async Task<Dictionary<string, MarketData>> FetchStocksAsync(
            IEnumerable<string> symbols, bool deep, CancellationToken ct)
        {
            var distinct = symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var results = await Task.WhenAll(
                distinct.Select(async s => (symbol: s, data: await FetchStockAsync(s, deep, ct))));
            return results
                .Where(r => r.data != null)
                .ToDictionary(r => r.symbol, r => r.data!);
        }

        public async Task<Dictionary<string, FundNavHistory>> FetchFundsAsync(
            IEnumerable<string> fundIds, CancellationToken ct)
        {
            var distinct = fundIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            var results = await Task.WhenAll(
                distinct.Select(async f => (fundId: f, nav: await fundData.GetFundNavHistoryAsync(f, ct))));
            return results
                .Where(r => r.nav != null)
                .ToDictionary(r => r.fundId, r => r.nav!);
        }

        public async Task<decimal> FetchUsdTwdRateAsync(CancellationToken ct) =>
            // No rate (the feed failed) is not cached, so the next valuation asks again; this one
            // uses the fallback.
            await cache.GetOrCreateSingleFlightAsync(FX_CACHE_KEY, CacheTtl.Portfolio.Fx, FetchUsdTwdRateUncachedAsync, ct)
                ?? FallbackUsdTwdRate;

        private async Task<decimal?> FetchUsdTwdRateUncachedAsync()
        {
            try
            {
                // BOT's flcsv endpoint now serves an anti-bot "Challenge Validation" HTML page to
                // non-browser clients, so it always fell back to 32. Use a stable key-less JSON feed.
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                var json = await client.GetStringAsync(FX_URL, CancellationToken.None);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                    rates.TryGetProperty("TWD", out var twd) &&
                    twd.TryGetDecimal(out var rate) && rate > 0)
                {
                    return rate;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch USD/TWD rate, using fallback 32");
            }

            return null;
        }

        public async Task InvalidateStockAsync(string symbol, CancellationToken ct)
        {
            cache.Remove(STOCK_CACHE_PREFIX + "deep_" + symbol);
            cache.Remove(STOCK_CACHE_PREFIX + "latest_" + symbol);

            var region = providerFactory.DetectRegion(symbol);
            var provider = providerFactory.GetMarketDataProvider(region);
            await provider.InvalidateAsync(symbol, ct);
        }
    }
}
