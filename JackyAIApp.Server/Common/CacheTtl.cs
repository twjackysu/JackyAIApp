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
        }
    }
}
