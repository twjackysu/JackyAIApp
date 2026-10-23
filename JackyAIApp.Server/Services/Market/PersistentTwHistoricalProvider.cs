using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using Microsoft.EntityFrameworkCore;
using TWStockLib;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// DB-backed decorator around <see cref="TWStockLibHistoricalProvider"/>: finalized daily bars
    /// are immutable and shared across all users, so once a (symbol, date) is persisted it is never
    /// refetched — only the gap between the DB's latest bar and the last completed trading day is
    /// pulled from TWStockLib.
    ///
    /// This exists because the app runs on Azure App Service's free tier, which cold-starts after
    /// ~20 min idle and wipes any <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
    /// entirely. For a personal-use app opened a few times a day, nearly every load is cold, so
    /// in-memory caching (PR #207) barely helps — but the Azure SQL Basic tier is DTU-provisioned
    /// and does NOT auto-pause on idle (only Serverless vCore tier does), so it stays warm across
    /// web cold starts. Persisting here is what actually survives a cold start; see
    /// .claude/plans/portfolio-db-price-cache.md for the full writeup.
    ///
    /// Phase 1 scope: TW stocks only (isin/STOCK_DAY are the dominant cost — see the plan doc).
    ///
    /// The primary gap-fill (TWStockLib's historical range endpoint) can occasionally still leave
    /// the single most recent trading day missing — e.g. a pre-market request before that endpoint
    /// has ingested yesterday's close, or a transient upstream failure. <see
    /// cref="BackfillLastTradingDayFromQuoteAsync"/> is a second, independent source for exactly
    /// that one day, using the realtime-quote endpoint's YesterdayClosingPrice instead.
    ///
    /// Uses <see cref="IDbContextFactory{TContext}"/> rather than an injected scoped DbContext:
    /// PortfolioService fans this out across many symbols via Task.WhenAll within one request, and
    /// a shared scoped DbContext isn't thread-safe for concurrent use — that combination briefly
    /// shipped and caused "A second operation was started on this context instance..." exceptions
    /// in prod, surfacing as spurious "報價失敗" on whichever symbols lost the race. Each FetchAsync
    /// call now creates its own short-lived context, so concurrent symbol fetches never share one.
    /// </summary>
    public class PersistentTwHistoricalProvider : IMarketDataProvider
    {
        private readonly ITWStockLibHistoricalProvider _inner;
        private readonly IDbContextFactory<AzureSQLDBContext> _dbFactory;
        private readonly ILogger<PersistentTwHistoricalProvider> _logger;
        private readonly ITradingCalendar _tradingCalendar;
        private readonly TimeProvider _timeProvider;

        private const string Market = "TW";
        internal const int LookbackMonths = 6;
        /// <summary>Shallow lookback for <see cref="FetchLatestAsync"/> — enough to cross a long
        /// weekend/holiday stretch and still land on the most recent finalized close. Adding a
        /// never-before-seen symbol only needs this, not the full 6-month window: valuation/day-
        /// change only ever look at the last couple of trading days (see PortfolioValuation.ValueAsOfDate),
        /// and every OTHER caller that genuinely needs deep history asks via FetchAsync instead.</summary>
        private const int LatestLookbackDays = 10;

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        /// <param name="inner">TWStockLib-backed source for the primary gap-fill and the quote fallback.</param>
        /// <param name="dbFactory">Creates a short-lived DbContext per call — see the class docs.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="tradingCalendar">Which day the stored history must reach: the last trading day before today.</param>
        /// <param name="timeProvider">Defaults to <see cref="TimeProvider.System"/>; overridable so
        /// <see cref="BackfillLastTradingDayFromQuoteAsync"/>'s morning cutoff is unit-testable
        /// instead of joining the rest of this codebase's untested wall-clock logic.</param>
        public PersistentTwHistoricalProvider(
            ITWStockLibHistoricalProvider inner,
            IDbContextFactory<AzureSQLDBContext> dbFactory,
            ILogger<PersistentTwHistoricalProvider> logger,
            ITradingCalendar tradingCalendar,
            TimeProvider? timeProvider = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tradingCalendar = tradingCalendar ?? throw new ArgumentNullException(nameof(tradingCalendar));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>Full ~6-month depth — for charts, technical indicators, and anything else that
        /// consumes more than the single latest price.</summary>
        public Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchInternalAsync(stockCode, deep: true, cancellationToken);

        /// <summary>Cheap latest-price fetch — see <see cref="LatestLookbackDays"/>. A never-before-
        /// seen symbol only pays for ~10 days of TWStockLib data instead of 6 months.</summary>
        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchInternalAsync(stockCode, deep: false, cancellationToken);

        /// <summary>
        /// Clears the persisted <see cref="StockSymbolInfo.TwMarketType"/>/<see cref="StockSymbolInfo.DeepFetchedAt"/>
        /// so the next fetch re-resolves the market from scratch instead of reusing a stuck bad
        /// value — <see cref="ResolveAndPersistMarketTypeAsync"/> otherwise treats any persisted
        /// <c>TwMarketType</c> as permanent and never re-checks it. Leaves <c>Name</c>/<c>Category</c>/
        /// <c>SubCategory</c> untouched (no reason to throw away an AI classification over this) and
        /// leaves already-persisted <see cref="DailyStockPrice"/> rows alone (harmless, re-fetches
        /// just dedupe against them).
        /// </summary>
        public async Task InvalidateAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var info = await db.StockSymbolInfos.FindAsync([stockCode], cancellationToken);
            if (info == null || (info.TwMarketType == null && info.DeepFetchedAt == null)) return;

            info.TwMarketType = null;
            info.DeepFetchedAt = null;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogDebug(ex, "Race invalidating market type for {Symbol}, ignoring", stockCode);
            }
        }

        private async Task<MarketData> FetchInternalAsync(string stockCode, bool deep, CancellationToken cancellationToken)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // Taipei's today, the exchange's own day: yesterday's bar counts as final from midnight there.
            var taipeiNow = _timeProvider.GetTaipeiNow();
            var today = DateOnly.FromDateTime(taipeiNow);
            var lastTradingDay = _tradingCalendar.PreviousTradingDay(today);
            // Always read whatever's stored within the full 6-month window — a few extra stored rows cost
            // nothing (no network call), and a shallow call benefits from a prior deep fetch if one happened.
            var windowStart = today.AddMonths(-LookbackMonths);

            var stored = await StoredBarsAsync(db, stockCode, windowStart, cancellationToken);
            var symbolInfo = await db.StockSymbolInfos.FindAsync([stockCode], cancellationToken);
            var companyName = symbolInfo?.Name;

            var fetchFrom = TwGapFill.Plan(
                deep, deep ? windowStart : today.AddDays(-LatestLookbackDays), LastDay(stored), lastTradingDay,
                deepFetchedBefore: symbolInfo?.DeepFetchedAt != null);
            if (fetchFrom is { } from)
            {
                var (done, fetchedName) = await GapFillAsync(db, stockCode, from, deep, taipeiNow, cancellationToken);
                if (done)
                {
                    companyName = fetchedName ?? companyName;
                    // Re-read so the returned list is the consistent, sorted merge of old + new.
                    stored = await StoredBarsAsync(db, stockCode, windowStart, cancellationToken);
                }
            }

            if (TwGapFill.ShouldBackfillFromQuote(LastDay(stored), lastTradingDay, taipeiNow))
            {
                await BackfillLastTradingDayFromQuoteAsync(db, stockCode, lastTradingDay, today, cancellationToken);
                stored = await StoredBarsAsync(db, stockCode, windowStart, cancellationToken);
            }

            return ToMarketData(stockCode, companyName, stored);
        }

        /// <summary>The stored bars from <paramref name="from"/> on, oldest first.</summary>
        private static Task<List<DailyStockPrice>> StoredBarsAsync(
            AzureSQLDBContext db, string stockCode, DateOnly from, CancellationToken ct) =>
            db.DailyStockPrices
                .Where(p => p.Symbol == stockCode && p.Market == Market && p.TradeDate >= from)
                .OrderBy(p => p.TradeDate)
                .ToListAsync(ct);

        private static DateOnly? LastDay(List<DailyStockPrice> bars) => bars.Count > 0 ? bars[^1].TradeDate : null;

        /// <summary>
        /// Fetches from <paramref name="from"/> to now and stores the finalized bars, the company name and,
        /// for a deep request, that the symbol has been deep-fetched. A failure is logged and the request
        /// is served from what is stored (Done is false) — matching TWStockLibHistoricalProvider's own
        /// fail-soft behavior.
        /// </summary>
        /// <returns>Whether it got through, and the company name the exchange gave (null if none).</returns>
        private async Task<(bool Done, string? CompanyName)> GapFillAsync(
            AzureSQLDBContext db, string stockCode, DateOnly from, bool deep, DateTime taipeiNow, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            try
            {
                var marketType = await ResolveAndPersistMarketTypeAsync(db, stockCode, ct);
                var fetched = await _inner.FetchRangeAsync(stockCode, from.ToDateTime(TimeOnly.MinValue), taipeiNow, marketType, ct);

                string? companyName = null;
                if (!string.IsNullOrWhiteSpace(fetched.CompanyName))
                {
                    companyName = fetched.CompanyName;
                    await PersistCompanyNameAsync(db, stockCode, fetched.CompanyName, ct);
                }

                await UpsertFinalizedBarsAsync(db, stockCode, fetched.HistoricalPrices, today, ct);

                // Only claim "deep fetched" once the fetch actually delivered history older than the
                // shallow window. Marking it purely because the fetch didn't throw is what let a thin
                // upstream response pin a symbol to a handful of days forever: no later deep call retried,
                // and every historical valuation before that gap silently valued the position at zero
                // (GetNetWorthHistoryAsync skips unpriceable positions but still emits the day) — a ~13.5M
                // phantom one-day jump on the net worth chart the moment the prices did start. A genuinely
                // new listing with less history re-attempts on each deep call: cheap, self-correcting once
                // it accumulates history, and strictly better than being wrong forever.
                if (deep && HasDeepCoverage(fetched.HistoricalPrices, today))
                    await MarkDeepFetchedAsync(db, stockCode, ct);

                return (true, companyName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gap-fill fetch failed for {Symbol}, serving DB-only data", stockCode);
                return (false, null);
            }
        }

        private static MarketData ToMarketData(string stockCode, string? companyName, List<DailyStockPrice> stored) => new()
        {
            StockCode = stockCode,
            CompanyName = companyName ?? "",
            HistoricalPrices = stored.Select(p => new DailyPrice
            {
                Date = p.TradeDate.ToDateTime(TimeOnly.MinValue),
                Open = p.Open,
                High = p.High,
                Low = p.Low,
                Close = p.Close,
                Volume = p.Volume,
            }).ToList(),
        };

        /// <summary>Whether a deep fetch's result actually reaches back past the shallow window,
        /// i.e. it really is deep history rather than the same few recent days a
        /// <see cref="FetchLatestAsync"/> would have produced. Guards <see cref="MarkDeepFetchedAsync"/>
        /// — see its call site for what marking too eagerly costs.</summary>
        private static bool HasDeepCoverage(List<DailyPrice> fetched, DateOnly today)
        {
            if (fetched.Count == 0) return false;
            var shallowStart = today.AddDays(-LatestLookbackDays);
            return fetched.Any(b => DateOnly.FromDateTime(b.Date) < shallowStart);
        }

        /// <summary>Marks that a deep fetch has run for this symbol at least once — see the
        /// backward fetch in <see cref="TwGapFill.Plan"/> for why this can't just
        /// be inferred from the oldest persisted row's date.</summary>
        private async Task MarkDeepFetchedAsync(AzureSQLDBContext db, string symbol, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info == null)
            {
                // ResolveAndPersistMarketTypeAsync creates the row whenever it actually resolved
                // the market, so the only way here is a symbol whose market is still unresolved —
                // deliberately unpersisted. Nothing to mark; don't throw over a missing marker.
                _logger.LogDebug("No StockSymbolInfo row found to mark deep-fetched for {Symbol}", symbol);
                return;
            }
            if (info.DeepFetchedAt != null) return;

            info.DeepFetchedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogDebug(ex, "Race persisting DeepFetchedAt for {Symbol}, ignoring", symbol);
            }
        }

        /// <summary>
        /// Returns null when the market could not be established. A persisted TwMarketType is
        /// treated as permanent (see the cache hit below), so writing an unresolved symbol's
        /// fallback here would pin it to the wrong market for good — which is exactly what
        /// happened to 8299 群聯 on 2026-08-28: absent from the isin list at that moment, recorded
        /// as TSE, and never priced again because it trades OTC. Leaving it unpersisted costs one
        /// (memory-cached) lookup per request and lets the symbol heal itself the next time the
        /// list does contain it.
        /// </summary>
        private async Task<MarketType?> ResolveAndPersistMarketTypeAsync(
            AzureSQLDBContext db, string symbol, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info != null && Enum.TryParse<MarketType>(info.TwMarketType, out var cached))
                return cached;

            // Falls through to TWStockLibHistoricalProvider's lookup in the stored listed-company
            // list (in memory, no network) for symbols not yet persisted here.
            var resolved = await _inner.DetermineMarketType(symbol);
            if (resolved == null)
            {
                _logger.LogWarning(
                    "Market type unresolved for {Symbol}; not persisting so it can be retried", symbol);
                return null;
            }

            if (info == null)
            {
                db.StockSymbolInfos.Add(new StockSymbolInfo
                {
                    Symbol = symbol,
                    Market = Market,
                    TwMarketType = resolved.Value.ToString(),
                    LastResolvedAt = DateTime.UtcNow,
                });
            }
            else
            {
                info.TwMarketType = resolved.Value.ToString();
                info.LastResolvedAt = DateTime.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Two concurrent cold-start requests resolving the same never-seen symbol can
                // race; the loser's insert fails the unique PK, which is fine — the winner's row
                // is what matters, we just return our own in-memory `resolved` either way.
                _logger.LogDebug(ex, "Race persisting market type for {Symbol}, ignoring", symbol);
            }

            return resolved;
        }

        private async Task PersistCompanyNameAsync(
            AzureSQLDBContext db, string symbol, string name, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info == null || info.Name == name) return;

            info.Name = name;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogDebug(ex, "Race persisting company name for {Symbol}, ignoring", symbol);
            }
        }

        /// <summary>Second-chance gap-fill for the single most recent trading day, via the
        /// realtime-quote endpoint's YesterdayClosingPrice rather than TWStockLib's historical
        /// range endpoint — see the call site. Never throws; a failure here just leaves the gap for
        /// the next request's normal gap-fill to close instead.</summary>
        private async Task BackfillLastTradingDayFromQuoteAsync(
            AzureSQLDBContext db, string symbol, DateOnly targetDate, DateOnly today, CancellationToken ct)
        {
            try
            {
                var quote = await _inner.GetLatestQuoteAsync(symbol, knownMarketType: null, ct);
                if (quote?.YesterdayClosingPrice is not > 0) return;
                // The quote's yesterday is the target day only once the quote is from a later session.
                // Until MIS moves on to the next day its yesterday is the day before the target, and
                // a stored day is never written again, so that close would stay wrong for good.
                if (quote.SyncTime is not { } syncTime || DateOnly.FromDateTime(TaipeiTime.ToTaipei(syncTime)) <= targetDate)
                {
                    _logger.LogDebug("Quote for {Symbol} is not from after {TargetDate}; leaving the gap", symbol, targetDate);
                    return;
                }

                var closePrice = quote.YesterdayClosingPrice.Value;
                var bar = new DailyPrice
                {
                    Date = targetDate.ToDateTime(TimeOnly.MinValue),
                    Open = closePrice,
                    High = closePrice,
                    Low = closePrice,
                    Close = closePrice,
                    Volume = 0,
                };
                await UpsertFinalizedBarsAsync(db, symbol, [bar], today, ct);
            }
            catch (Exception ex)
            {
                // Warning, not debug: unlike the "race, ignore" cases elsewhere in this class, this
                // one leaves a real hole in the price history that nothing else fills in.
                _logger.LogWarning(ex, "Quote-based backfill failed for {Symbol}, leaving gap for next fetch", symbol);
            }
        }

        private Task UpsertFinalizedBarsAsync(
            AzureSQLDBContext db, string symbol, List<DailyPrice> bars, DateOnly today, CancellationToken ct) =>
            DailyStockPriceWriter.UpsertFinalizedAsync(db, symbol, bars, today, _logger, ct);
    }
}
