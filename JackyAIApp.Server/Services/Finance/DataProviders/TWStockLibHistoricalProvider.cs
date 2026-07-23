using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.Extensions.Caching.Memory;
using TWStockLib.Models;
using TWStockLib.Services;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Fetches historical price data using TWStockLib NuGet package.
    /// Provides OHLCV data for technical indicator calculations.
    /// </summary>
    public class TWStockLibHistoricalProvider : IMarketDataProvider, ITWStockLibHistoricalProvider
    {
        private readonly IStockMarketService _stockService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TWStockLibHistoricalProvider> _logger;

        /// <summary>Default lookback period in months for historical data</summary>
        private const int DEFAULT_LOOKBACK_MONTHS = 6;
        /// <summary>Shallow lookback for FetchLatestAsync — enough to cross a long weekend/holiday
        /// stretch and still land on the most recent finalized close.</summary>
        private const int LATEST_LOOKBACK_DAYS = 10;
        private const string StockListCacheKeyPrefix = "twstocklist:";

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        public TWStockLibHistoricalProvider(
            IStockMarketService stockService,
            IMemoryCache cache,
            ILogger<TWStockLibHistoricalProvider> logger)
        {
            _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchRangeAsync(stockCode, DateTime.Now.AddMonths(-DEFAULT_LOOKBACK_MONTHS), DateTime.Now,
                knownMarketType: null, cancellationToken);

        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchRangeAsync(stockCode, DateTime.Now.AddDays(-LATEST_LOOKBACK_DAYS), DateTime.Now,
                knownMarketType: null, cancellationToken);

        /// <summary>
        /// Fetch a specific date range rather than the fixed <see cref="DEFAULT_LOOKBACK_MONTHS"/>
        /// window. Used directly by <c>PersistentTwHistoricalProvider</c> for gap-only fetches
        /// (only the days missing from its DB cache) instead of always re-pulling the full window.
        /// </summary>
        /// <param name="stockCode">TW stock code.</param>
        /// <param name="startDate">Range start (inclusive).</param>
        /// <param name="endDate">Range end (inclusive).</param>
        /// <param name="knownMarketType">Skip re-resolving TSE/OTC when the caller already knows
        /// it (e.g. from its own DB-persisted resolution) — avoids a redundant lookup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<MarketData> FetchRangeAsync(
            string stockCode, DateTime startDate, DateTime endDate,
            MarketType? knownMarketType, CancellationToken cancellationToken = default)
        {
            var marketData = new MarketData { StockCode = stockCode };

            try
            {
                // Determine market type (TSE or OTC)
                var marketType = knownMarketType ?? await DetermineMarketType(stockCode);

                _logger.LogInformation(
                    "Fetching historical data for {stockCode} from {start:yyyy-MM-dd} to {end:yyyy-MM-dd} on {market}",
                    stockCode, startDate, endDate, marketType);

                var historyResult = await _stockService.GetHistoricalDataAsync(
                    stockCode, startDate, endDate, marketType, cancellationToken);

                if (!historyResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "GetHistoricalDataAsync failed for {stockCode}: {errorCode} - {errorMessage}",
                        stockCode, historyResult.ErrorCode, historyResult.ErrorMessage);
                    return marketData;
                }

                var historyList = historyResult.Value!
                    .Where(h => h.ClosingPrice > 0) // Filter out invalid entries
                    .OrderBy(h => h.Date)
                    .ToList();

                if (historyList.Count == 0)
                {
                    _logger.LogWarning("No historical data returned for stock {stockCode}", stockCode);
                    return marketData;
                }

                marketData.HistoricalPrices = historyList.Select(h => new DailyPrice
                {
                    Date = h.Date,
                    Open = h.OpeningPrice,
                    High = h.HighestPrice,
                    Low = h.LowestPrice,
                    Close = h.ClosingPrice,
                    Volume = h.TradeVolume,
                    Turnover = (long)h.TurnOverInValue,
                    Transactions = (int)h.NumberOfDeals
                }).ToList();

                // Try to get company name from realtime quote
                try
                {
                    var quote = await GetCachedRealtimeQuoteAsync(stockCode, marketType, cancellationToken);
                    if (quote != null)
                    {
                        marketData.CompanyName = quote.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not fetch realtime quote for company name, skipping");
                }

                _logger.LogInformation(
                    "Fetched {count} historical records for {stockCode} ({name})",
                    marketData.HistoricalPrices.Count, stockCode, marketData.CompanyName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch historical data for stock {stockCode}", stockCode);
            }

            return marketData;
        }

        /// <summary>
        /// Determines whether a stock is on TSE or OTC market via the combined TSE+OTC list.
        /// Public (via <see cref="ITWStockLibHistoricalProvider"/>) so <c>PersistentTwHistoricalProvider</c>
        /// can reuse this memory-cached resolution instead of duplicating the isin-list lookup
        /// logic when it needs to resolve a symbol it hasn't persisted to <c>StockSymbolInfo</c> yet.
        /// Previously issued two sequential per-market calls (TSE then OTC) and silently fell back
        /// to TSE — with no log line — whenever either call soft-failed (IsSuccess:false, which
        /// <see cref="GetCachedAllStockListAsync"/> now logs). One combined call halves that
        /// failure surface.
        /// </summary>
        public async Task<MarketType> DetermineMarketType(string stockCode)
        {
            try
            {
                var allList = await GetCachedAllStockListAsync();
                if (allList != null && allList.TryGetValue(stockCode, out var info))
                    return info.Market;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine market type for {stockCode}, defaulting to TSE", stockCode);
                return MarketType.TSE;
            }

            _logger.LogWarning(
                "{stockCode} not found in combined TWStockLib stock list, defaulting to TSE", stockCode);
            return MarketType.TSE;
        }

        /// <summary>
        /// The underlying TWSE endpoint (isin.twse.com.tw) returns the *entire* listed-company
        /// table on every call and is slow (multi-second) — without caching, a portfolio with N
        /// distinct TW symbols pays that cost N times per request, which dominates the wall time.
        /// Membership only changes on IPOs/delistings, so a long TTL is safe.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, StockData>?> GetCachedAllStockListAsync()
        {
            const string key = StockListCacheKeyPrefix + "all";
            if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, StockData>? cached))
                return cached;

            var result = await _stockService.GetAllStockListAsync();
            if (!result.IsSuccess)
            {
                // Don't cache a failed lookup — the next call should retry rather than being
                // stuck defaulting to TSE for a whole day. Logged (unlike the old per-market
                // path) so a soft failure here is actually diagnosable.
                _logger.LogWarning(
                    "GetAllStockListAsync failed: {errorCode} - {errorMessage}",
                    result.ErrorCode, result.ErrorMessage);
                return null;
            }

            var list = result.Value!;
            _cache.Set(key, list, CacheTtl.Finance.TwStockList);
            return list;
        }

        /// <summary>
        /// Realtime quote for a TW symbol, short-cached so this call and Portfolio's own intraday
        /// price-overlay lookup (<c>PortfolioService.GetTwRealtimeQuoteAsync</c>) share one network
        /// hit instead of each fetching it independently. Tries the resolved market first, then the
        /// other one, same as Portfolio's version — kept consistent since they now share a cache.
        /// </summary>
        private async Task<StockQuote?> GetCachedRealtimeQuoteAsync(
            string stockCode, MarketType primaryMarket, CancellationToken ct)
        {
            var key = CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + stockCode;
            if (_cache.TryGetValue(key, out StockQuote? cached))
                return cached;

            var secondaryMarket = primaryMarket == MarketType.TSE ? MarketType.OTC : MarketType.TSE;
            var quoteResult = await _stockService.GetRealtimeQuoteAsync(stockCode, primaryMarket, ct);
            var quote = quoteResult.IsSuccess ? quoteResult.Value : null;
            if (quote == null)
            {
                var fallback = await _stockService.GetRealtimeQuoteAsync(stockCode, secondaryMarket, ct);
                quote = fallback.IsSuccess ? fallback.Value : null;
            }

            if (quote != null)
                _cache.Set(key, quote, CacheTtl.Finance.TwRealtimeQuote);
            return quote;
        }
    }
}
