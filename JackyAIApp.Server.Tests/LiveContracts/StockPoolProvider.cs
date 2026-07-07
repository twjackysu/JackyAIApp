namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Curated stocks used by live contract tests that hit a per-symbol endpoint (Yahoo quotes,
    /// TWSE fundamentals, SEC facts). Testing every symbol every run would be slow and impolite to
    /// a free public API; testing only one symbol risks a coincidental pass if that one symbol
    /// happens to have missing data. Instead each run deterministically samples a few symbols for
    /// that calendar day (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section 4.3) — reruns on the same day
    /// pick the same symbols (easy to reproduce a failure), and the sample rotates through the
    /// whole pool over time.
    /// </summary>
    public static class StockPoolProvider
    {
        /// <summary>TW-listed large caps across sectors — chosen to be liquid, long-lived tickers unlikely to be delisted.</summary>
        public static readonly IReadOnlyList<string> TwStocks =
        [
            "2330", "2317", "2454", "2412", "2308", // semis/electronics/telecom
            "2882", "2891", "2886", // financials
            "1301", "1216", "2603", // industrials/shipping
            "0050", "00878", // ETFs
        ];

        /// <summary>US large caps across sectors, same liquidity/longevity rationale as <see cref="TwStocks"/>.</summary>
        public static readonly IReadOnlyList<string> UsStocks =
        [
            "AAPL", "MSFT", "NVDA", "GOOGL", "AMZN", "META", // tech
            "JPM", "V", // financials
            "JNJ", "UNH", // healthcare
            "WMT", "PG", "XOM", // consumer/energy
        ];

        /// <summary>
        /// Deterministically picks <paramref name="count"/> symbols from <paramref name="pool"/>
        /// for "today" (UTC). <paramref name="seedOffset"/> should differ between independent
        /// pools/integrations sampled on the same day so their picks don't correlate
        /// (e.g. TW=1, US=2) — plain ints, not string hashing, since string.GetHashCode() is
        /// randomized per-process in .NET and would break same-day reproducibility.
        /// </summary>
        public static IReadOnlyList<string> PickForToday(IReadOnlyList<string> pool, int count, int seedOffset)
        {
            var dateSeed = int.Parse(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyyMMdd"));
            var rng = new Random(dateSeed + seedOffset);
            return pool.OrderBy(_ => rng.Next()).Take(Math.Min(count, pool.Count)).ToList();
        }
    }
}
