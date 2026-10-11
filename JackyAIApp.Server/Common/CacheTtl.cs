namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Named cache durations, grouped by module, to avoid magic numbers and give a single
    /// place to see (and tune) how long each external data source is trusted before refetching.
    ///
    /// Note: most Finance data-provider TTLs (SEC fundamentals, TWSE macro, Yahoo quotes, ...)
    /// are intentionally distinct per-source freshness policies already named as a local
    /// `CACHE_HOURS` const in their own class — those are left alone here rather than collapsed
    /// into shared constants, since doing so would either be purely cosmetic or risk silently
    /// changing a deliberately-tuned value. This class only covers durations that were genuinely
    /// duplicated as unnamed magic numbers.
    /// </summary>
    public static class CacheTtl
    {
        public static class Portfolio
        {
            /// <summary>Live stock price lookups (summary, net-worth chart, detail drawer).</summary>
            public static readonly TimeSpan StockPrice = TimeSpan.FromMinutes(30);

            /// <summary>USD/TWD FX rate.</summary>
            public static readonly TimeSpan Fx = TimeSpan.FromHours(4);

            /// <summary>FundRich fund search results and NAV history.</summary>
            public static readonly TimeSpan FundRich = TimeSpan.FromHours(6);

            /// <summary>Dividend/distribution history (stock + fund) — doesn't change intraday.</summary>
            public static readonly TimeSpan Dividend = TimeSpan.FromHours(24);

            /// <summary>The 回落煞車 market signal, one copy for the whole site. A refresh or a
            /// background store of new days invalidates it early, so this only bounds how long an
            /// unchanged signal is served without re-reading the database.</summary>
            public static readonly TimeSpan DrawdownBrakeSignal = TimeSpan.FromSeconds(60);
        }

        public static class Finance
        {
            /// <summary>TW realtime quote (mis.twse.com.tw). Two independent call sites want the
            /// same symbol's quote in the same request — <c>TWStockLibHistoricalProvider</c> (for
            /// the company name) and Portfolio's intraday price overlay — so a short TTL is enough
            /// to dedupe them without meaningfully staling a "live" price.</summary>
            public static readonly TimeSpan TwRealtimeQuote = TimeSpan.FromSeconds(10);

            /// <summary>Cache key prefix for <see cref="TwRealtimeQuote"/> — shared verbatim by
            /// both call sites above so they land on the same cache entry; must match exactly.</summary>
            public const string TwRealtimeQuoteCacheKeyPrefix = "twquote:";
        }
    }
}
