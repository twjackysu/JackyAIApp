using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.ExternalApi;
using Microsoft.Extensions.Caching.Memory;
using TWStockLib;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Fetches historical price data using TWStockLib NuGet package.
    /// Provides OHLCV data for technical indicator calculations.
    /// </summary>
    public class TWStockLibHistoricalProvider : IMarketDataProvider, ITWStockLibHistoricalProvider
    {
        private readonly IStockMarketService _stockService;
        private readonly ITwListedCompanyService _listedCompanies;
        private readonly IMemoryCache _cache;
        private readonly IThirdPartyGateway _gateway;
        private readonly ILogger<TWStockLibHistoricalProvider> _logger;
        private readonly TimeProvider _time;

        /// <summary>Default lookback period in months for historical data</summary>
        private const int DEFAULT_LOOKBACK_MONTHS = 6;
        /// <summary>Shallow lookback for FetchLatestAsync — enough to cross a long weekend/holiday
        /// stretch and still land on the most recent finalized close.</summary>
        private const int LATEST_LOOKBACK_DAYS = 10;

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        public TWStockLibHistoricalProvider(
            IStockMarketService stockService,
            ITwListedCompanyService listedCompanies,
            IMemoryCache cache,
            IThirdPartyGateway gateway,
            ILogger<TWStockLibHistoricalProvider> logger,
            TimeProvider? timeProvider = null)
        {
            _stockService = stockService ?? throw new ArgumentNullException(nameof(stockService));
            _listedCompanies = listedCompanies ?? throw new ArgumentNullException(nameof(listedCompanies));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _time = timeProvider ?? TimeProvider.System;
        }

        // The ranges end at today in Taipei, the exchange's own day.
        public Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var now = _time.GetTaipeiNow();
            return FetchRangeAsync(stockCode, now.AddMonths(-DEFAULT_LOOKBACK_MONTHS), now, knownMarketType: null, cancellationToken);
        }

        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            var now = _time.GetTaipeiNow();
            return FetchRangeAsync(stockCode, now.AddDays(-LATEST_LOOKBACK_DAYS), now, knownMarketType: null, cancellationToken);
        }

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
                // Unresolved falls back to TSE only for this one attempt — nothing here persists
                // it, so a wrong guess costs a failed fetch, not a stuck symbol.
                var marketType = knownMarketType ?? await DetermineMarketType(stockCode) ?? MarketType.TSE;

                _logger.LogInformation(
                    "Fetching historical data for {stockCode} from {start:yyyy-MM-dd} to {end:yyyy-MM-dd} on {market}",
                    stockCode, startDate, endDate, marketType);

                var historyResult = await GetHistoryByMonthAsync(
                    stockCode, startDate, endDate, marketType, cancellationToken);

                if (!historyResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "GetHistoricalDataAsync failed for {stockCode}: {errorCode} - {errorMessage}",
                        stockCode, historyResult.ErrorCode, historyResult.ErrorMessage);
                    return marketData;
                }

                var historyList = historyResult.Value!
                    .Where(h => h.ClosingPrice is > 0) // a day without trades has no prices
                    .OrderBy(h => h.Date)
                    .ToList();

                if (historyList.Count == 0)
                {
                    _logger.LogWarning("No historical data returned for stock {stockCode}", stockCode);
                    return marketData;
                }

                marketData.HistoricalPrices = historyList.Select(ToDailyPrice).ToList();

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
        /// The range as the exchange serves it: one request per month, a whole month whatever the range
        /// asked for. Each month goes through the <see cref="IThirdPartyGateway"/>, so a long range is
        /// paced instead of fired off at once, and two callers who need the same stock-month at the same
        /// time share one request.
        ///
        /// All or nothing: if any month fails the whole range fails, as it did when TWStockLib fetched the
        /// range in one call. <c>PersistentTwHistoricalProvider</c> relies on that — a partly successful
        /// history would let it mark a symbol's history complete with a month missing in the middle.
        /// </summary>
        private async Task<StockResult<IReadOnlyList<StockHistory>>> GetHistoryByMonthAsync(
            string stockCode, DateTime startDate, DateTime endDate, MarketType marketType, CancellationToken ct)
        {
            var requests = new List<Task<StockResult<IReadOnlyList<StockHistory>>>>();
            for (var month = new DateTime(startDate.Year, startDate.Month, 1); month <= endDate; month = month.AddMonths(1))
                requests.Add(RequestMonthAsync(stockCode, month, marketType, GatewayPriority.Interactive, ct));

            var results = await Task.WhenAll(requests);
            var failed = results.FirstOrDefault(r => !r.IsSuccess);
            if (failed is not null) return failed;

            // The same cut TWStockLib made when it was handed the range itself.
            IReadOnlyList<StockHistory> bars =
                [.. results.SelectMany(r => r.Value!).Where(h =>
                    h.Date.ToDateTime(TimeOnly.MinValue) >= startDate && h.Date.ToDateTime(TimeOnly.MinValue) <= endDate)];
            return StockResult<IReadOnlyList<StockHistory>>.Ok(bars);
        }

        /// <summary>One whole month of one stock, through the gateway on the lane of its market.
        /// Keyed per symbol-month, so every caller wanting the same month at once shares one request.</summary>
        private Task<StockResult<IReadOnlyList<StockHistory>>> RequestMonthAsync(
            string stockCode, DateTime monthStart, MarketType marketType, GatewayPriority priority, CancellationToken ct)
        {
            var source = marketType == MarketType.OTC ? ThirdPartySources.TpexHistory : ThirdPartySources.TwseHistory;
            var first = new DateTime(monthStart.Year, monthStart.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            return _gateway.RunAsync(
                source, $"{marketType}:{stockCode}:{first:yyyyMM}", priority,
                token => _stockService.GetHistoricalDataAsync(
                    stockCode, marketType, DateOnly.FromDateTime(first), DateOnly.FromDateTime(last), token), ct);
        }

        /// <summary>
        /// A traded day as a bar. Volume is shares and turnover NT$ on both exchanges: TWStockLib
        /// converts TPEx's lots and thousands of NT$ (since 2.3). Only days with a close get here,
        /// and such a day has all four prices; the close stands in for one missing all the same.
        /// </summary>
        private static DailyPrice ToDailyPrice(StockHistory h)
        {
            var close = h.ClosingPrice!.Value;
            return new DailyPrice
            {
                Date = h.Date.ToDateTime(TimeOnly.MinValue),
                Open = h.OpeningPrice ?? close,
                High = h.HighestPrice ?? close,
                Low = h.LowestPrice ?? close,
                Close = close,
                Volume = h.TradeVolume,
                Turnover = (long)h.TurnoverValue,
                Transactions = (int)h.NumberOfDeals,
            };
        }

        public async Task<TwMonthBars> FetchMonthAsync(
            string stockCode, DateOnly month, MarketType marketType, GatewayPriority priority,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await RequestMonthAsync(
                    stockCode, month.ToDateTime(TimeOnly.MinValue), marketType, priority, cancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Month {month:yyyy-MM} of {stockCode} failed: {errorCode} - {errorMessage}",
                        month, stockCode, result.ErrorCode, result.ErrorMessage);
                    return TwMonthBars.Failed;
                }

                var bars = result.Value!
                    .Where(h => h.ClosingPrice is > 0)
                    .OrderBy(h => h.Date)
                    .Select(ToDailyPrice)
                    .ToList();
                return new TwMonthBars(true, bars);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A gateway timeout or a throwing fetch: this month did not arrive, which is a
                // different thing from arriving empty.
                _logger.LogWarning(ex, "Month {month:yyyy-MM} of {stockCode} could not be fetched", month, stockCode);
                return TwMonthBars.Failed;
            }
        }

        /// <summary>
        /// Determines whether a stock is on TSE or OTC from the stored listed-company list
        /// (<see cref="ITwListedCompanyService"/>, an in-memory lookup). Public (via
        /// <see cref="ITWStockLibHistoricalProvider"/>) so <c>PersistentTwHistoricalProvider</c>
        /// can resolve a symbol it hasn't persisted to <c>StockSymbolInfo</c> yet.
        ///
        /// This used to come from TWStockLib's GetAllStockListAsync, a scrape of
        /// isin.twse.com.tw: ~12MB of HTML that measured 76s from Taiwan against a 30s client
        /// timeout. A failure wasn't cached and every TW symbol started its own download, and the
        /// realtime-quote overlay asks this before every quote from 09:00 on — so every TW price
        /// fetch waited out the timeout, and the portfolio summary took 30s+ each time its
        /// 30-minute price cache expired.
        /// </summary>
        public async Task<MarketType?> DetermineMarketType(string stockCode)
        {
            try
            {
                if (await _listedCompanies.GetMarketTypeAsync(stockCode) is { } market)
                    return market;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to determine market type for {stockCode}", stockCode);
                return null;
            }

            // Returns null rather than TSE. This used to answer TSE on failure, which made a guess
            // indistinguishable from a real lookup — PersistentTwHistoricalProvider stored that
            // answer permanently, so an OTC symbol missing from the list on the one day it was
            // first seen (8299 群聯, 2026-08-28) was pinned to the wrong market forever and never
            // priced again. Callers that only need something to try can fall back themselves;
            // callers that persist must not.
            _logger.LogWarning(
                "{stockCode} not found in the TW listed-company list; market type unresolved", stockCode);
            return null;
        }

        /// <inheritdoc/>
        public async Task<StockQuote?> GetLatestQuoteAsync(
            string stockCode, MarketType? knownMarketType, CancellationToken cancellationToken = default)
        {
            // Transient fallback only (nothing persisted here); GetCachedRealtimeQuoteAsync tries
            // the other market anyway when the first comes back empty, so an unresolved symbol
            // still quotes correctly.
            var marketType = knownMarketType ?? await DetermineMarketType(stockCode) ?? MarketType.TSE;
            return await GetCachedRealtimeQuoteAsync(stockCode, marketType, cancellationToken);
        }

        /// <summary>
        /// Realtime quote for a TW symbol, short-cached so this call and
        /// <see cref="GetLatestQuoteAsync"/> (used by <c>TwTodayOverlayMarketDataProvider</c> for
        /// its intraday price-overlay) share one network hit instead of each fetching it
        /// independently. Tries the resolved market first, then the other one.
        /// </summary>
        private Task<StockQuote?> GetCachedRealtimeQuoteAsync(
            string stockCode, MarketType primaryMarket, CancellationToken ct) =>
            // A portfolio and an analysis open at once ask for the same quote: one fetch for both.
            // No quote (neither market answered) is not cached, so the next call tries again.
            _cache.GetOrCreateSingleFlightAsync(
                CacheTtl.Finance.TwRealtimeQuoteCacheKeyPrefix + stockCode, CacheTtl.Finance.TwRealtimeQuote,
                () => FetchRealtimeQuoteAsync(stockCode, primaryMarket), ct);

        private async Task<StockQuote?> FetchRealtimeQuoteAsync(string stockCode, MarketType primaryMarket)
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            var ct = CancellationToken.None;
            var secondaryMarket = primaryMarket == MarketType.TSE ? MarketType.OTC : MarketType.TSE;
            var quoteResult = await _stockService.GetRealtimeQuoteAsync(stockCode, primaryMarket, ct);
            var quote = quoteResult.IsSuccess ? quoteResult.Value : null;
            if (quote == null)
            {
                var fallback = await _stockService.GetRealtimeQuoteAsync(stockCode, secondaryMarket, ct);
                quote = fallback.IsSuccess ? fallback.Value : null;
            }

            return quote;
        }
    }
}
