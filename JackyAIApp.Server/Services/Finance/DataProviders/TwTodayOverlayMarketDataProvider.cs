using JackyAIApp.Server.DTO.Finance;
using TWStockLib.Models;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// Outermost decorator for the TW <see cref="IMarketDataProvider"/> chain: overlays today's
    /// price on top of whatever the wrapped chain returns, which — via
    /// <see cref="PersistentTwHistoricalProvider"/> — never includes today's bar, by design (that
    /// class never persists "today" until the calendar date rolls over).
    ///
    /// Must sit OUTSIDE <see cref="CachedMarketDataProvider"/>'s 4-hour cache, not inside it: baking
    /// today's still-moving price into that cache would freeze it for up to 4 hours. This layer
    /// applies its own overlay fresh on every call instead — the underlying quote itself is
    /// short-cached (10s, see <see cref="ITWStockLibHistoricalProvider.GetLatestQuoteAsync"/>), so
    /// this costs nothing extra.
    ///
    /// Previously this exact logic lived duplicated inside PortfolioService only, so Finance's own
    /// stock-analysis pages (which call <see cref="IMarketDataProvider"/> directly, not through
    /// Portfolio) never got it and silently showed yesterday's close from close (13:30) through
    /// midnight — same bug, different call site, unnoticed. Living here as the actual DI
    /// registration for <see cref="IMarketDataProvider"/> (TW) fixes both call sites at once.
    ///
    /// Also backfills yesterday's closing bar pre-market (before 09:00) when `_inner`'s DB-backed
    /// chain hasn't ingested it yet — see <see cref="BackfillYesterdayIfMissing"/>.
    /// </summary>
    public class TwTodayOverlayMarketDataProvider : IMarketDataProvider
    {
        private readonly IMarketDataProvider _inner;
        private readonly ITWStockLibHistoricalProvider _quoteSource;
        private readonly ILogger<TwTodayOverlayMarketDataProvider> _logger;
        private readonly TimeProvider _timeProvider;

        public DataProviderType Type => _inner.Type;

        /// <param name="inner">The wrapped chain (DB layer + 4h cache) whose result gets today's
        /// price overlaid on top.</param>
        /// <param name="quoteSource">Single source of the realtime/final quote used for the
        /// overlay.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="timeProvider">Defaults to <see cref="TimeProvider.System"/>; overridable so
        /// <see cref="IsTodayTwQuoteWindow"/>'s time-of-day gate — the exact thing that went through
        /// several wrong assumptions before landing here — is actually unit-testable instead of
        /// joining the rest of this codebase's untested wall-clock logic.</param>
        public TwTodayOverlayMarketDataProvider(
            IMarketDataProvider inner,
            ITWStockLibHistoricalProvider quoteSource,
            ILogger<TwTodayOverlayMarketDataProvider> logger,
            TimeProvider? timeProvider = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _quoteSource = quoteSource ?? throw new ArgumentNullException(nameof(quoteSource));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchWithOverlayAsync(stockCode, deep: true, cancellationToken);

        public Task<MarketData> FetchLatestAsync(string stockCode, CancellationToken cancellationToken = default) =>
            FetchWithOverlayAsync(stockCode, deep: false, cancellationToken);

        public Task InvalidateAsync(string stockCode, CancellationToken cancellationToken = default) =>
            _inner.InvalidateAsync(stockCode, cancellationToken);

        private async Task<MarketData> FetchWithOverlayAsync(string stockCode, bool deep, CancellationToken ct)
        {
            var data = deep ? await _inner.FetchAsync(stockCode, ct) : await _inner.FetchLatestAsync(stockCode, ct);

            var taipeiNow = _timeProvider.GetUtcNow().UtcDateTime.AddHours(8);
            if (taipeiNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return data;

            try
            {
                var quote = await _quoteSource.GetLatestQuoteAsync(stockCode, knownMarketType: null, ct);
                if (quote == null) return data;

                // Pre-market (before 09:00) backfill: `_inner`'s DB-backed chain can lag a
                // calendar day behind — TWSE's own closing data isn't always ingested until well
                // after midnight, so a request at e.g. 07:30 can still be missing yesterday's
                // bar. The quote endpoint's YesterdayClosingPrice is populated the moment the
                // exchange rolls the trading day, well before 09:00, so it's a reliable source to
                // fill that specific gap — independently of the live "today" overlay below, which
                // only starts at market open.
                data = BackfillYesterdayIfMissing(data, quote, taipeiNow);

                if (IsTodayTwQuoteWindow(taipeiNow))
                {
                    var livePrice = EffectiveLivePrice(quote);
                    if (livePrice is > 0)
                        data = WithRealtimeOverlay(data, quote, livePrice.Value);
                }
            }
            catch (Exception ex)
            {
                // Never let a quote-fetch failure break the whole request — the caller always has
                // the historical-close fallback already in `data`.
                _logger.LogDebug(ex, "Realtime quote overlay failed for {StockCode}, using historical close", stockCode);
            }

            return data;
        }

        /// <summary>Whether a same-day TW quote should be trusted as authoritative right now: from
        /// market open (09:00) through the rest of the calendar day, not just the 09:00-13:30
        /// session — TWStockLib's realtime-quote endpoint keeps returning the day's final matched
        /// price for hours after the 13:30 close (verified against live data), and nothing else in
        /// the pipeline ever surfaces that finalized price otherwise. Before 09:00 the quote would
        /// still be showing yesterday's stale price, which <see cref="WithRealtimeOverlay"/> would
        /// incorrectly stamp as "today", so the lower bound stays. No upper bound: unlike an earlier
        /// version of this gate (09:00-13:30 only), deliberately not cutting off at market close —
        /// the quote endpoint has been observed still returning the correct final price hours later,
        /// and there's no correctness reason to stop trusting it, only an unconfirmed worry that it
        /// might eventually go empty late at night. Revisit if that's ever actually observed. Taiwan
        /// has no DST, so a fixed UTC+8 offset is exact — no TimeZoneInfo lookup needed. Does not
        /// account for exchange holidays: a holiday just means the quote below comes back stale/
        /// NOT_FOUND, silently ignored in favor of the historical close.</summary>
        private static bool IsTodayTwQuoteWindow(DateTime taipeiNow) =>
            taipeiNow.TimeOfDay >= new TimeSpan(9, 0, 0);

        /// <summary>Fills in yesterday's closing bar from the quote's YesterdayClosingPrice when
        /// `_inner`'s historical chain doesn't already have it — see the call site for why this
        /// gap exists. Appends the expected-last-trading-day bar (never touches "today"'s slot,
        /// which <see cref="WithRealtimeOverlay"/> owns) only when it's actually missing, so this
        /// is a no-op once the DB catches up. Same weekend-blind approximation as the rest of this
        /// class: a real exchange holiday just means the backfilled date is wrong by a day or two,
        /// silently self-correcting once the DB ingests the real data.</summary>
        private static MarketData BackfillYesterdayIfMissing(MarketData source, StockQuote quote, DateTime taipeiNow)
        {
            if (quote.YesterdayClosingPrice is not > 0) return source;

            var expectedDate = LastBusinessDayBefore(taipeiNow.Date);
            var lastBar = source.HistoricalPrices.Count > 0 ? source.HistoricalPrices[^1] : null;
            if (lastBar != null && lastBar.Date.Date >= expectedDate) return source;

            var closePrice = quote.YesterdayClosingPrice.Value;
            var bar = new DailyPrice
            {
                Date = expectedDate,
                Open = closePrice,
                High = closePrice,
                Low = closePrice,
                Close = closePrice,
                Volume = 0,
            };

            var prices = new List<DailyPrice>(source.HistoricalPrices) { bar };
            return new MarketData
            {
                StockCode = source.StockCode,
                CompanyName = source.CompanyName,
                HistoricalPrices = prices,
                Fundamentals = source.Fundamentals,
                Chips = source.Chips,
                FetchedAt = DateTime.UtcNow,
            };
        }

        private static DateTime LastBusinessDayBefore(DateTime date)
        {
            var d = date.AddDays(-1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }

        /// <summary>TWSE's realtime "last matched price" (<see cref="StockQuote.LastPrice"/>) is
        /// frequently empty mid-session — falls back to the bid/ask midpoint (or whichever side is
        /// present) so the overlay still fires; returns null only when the quote carries no usable
        /// price at all.</summary>
        private static decimal? EffectiveLivePrice(StockQuote quote)
        {
            if (quote.LastPrice is > 0) return quote.LastPrice;

            var bid = quote.Top5BuyPrice.Length > 0 ? quote.Top5BuyPrice[0] : 0;
            var ask = quote.Top5SellPrice.Length > 0 ? quote.Top5SellPrice[0] : 0;
            if (bid > 0 && ask > 0) return Math.Round((bid + ask) / 2, 2);
            if (bid > 0) return bid;
            if (ask > 0) return ask;
            return null;
        }

        /// <summary>Returns a shallow clone of <paramref name="source"/> with today's bar
        /// replaced/appended from a realtime quote, so <c>HistoricalPrices.LastOrDefault()</c>
        /// reflects the live price. Never mutates <paramref name="source"/> itself — it may be the
        /// same object instance cached (up to 4h) by <see cref="CachedMarketDataProvider"/>.</summary>
        private static MarketData WithRealtimeOverlay(MarketData source, StockQuote quote, decimal livePrice)
        {
            var today = DateTime.Today;
            var todayBar = new DailyPrice
            {
                Date = today,
                Open = quote.OpeningPrice ?? livePrice,
                High = quote.HighestPrice ?? livePrice,
                Low = quote.LowestPrice ?? livePrice,
                Close = livePrice,
                Volume = quote.TotalVolume ?? 0,
            };

            var prices = new List<DailyPrice>(source.HistoricalPrices);
            if (prices.Count > 0 && prices[^1].Date.Date == today)
                prices[^1] = todayBar;
            else
                prices.Add(todayBar);

            return new MarketData
            {
                StockCode = source.StockCode,
                CompanyName = source.CompanyName,
                HistoricalPrices = prices,
                Fundamentals = source.Fundamentals,
                Chips = source.Chips,
                FetchedAt = DateTime.UtcNow,
            };
        }
    }
}
