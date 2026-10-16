using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// One instrument's price history, sorted once so "the last close on or before day D" is a
    /// binary search instead of a filter + sort over the whole history for every day × position
    /// cell of a valuation loop.
    ///
    /// Built per request and never written back: the price lists it is built from are cached,
    /// shared instances (FundDataService up to 6h, the stock providers up to 4h), so the source is
    /// only enumerated, never reordered in place.
    ///
    /// Semantics are exactly those of the LINQ it replaces,
    /// <c>Where(day &lt;= D).OrderByDescending(date).FirstOrDefault()</c>: the series is kept in
    /// the stable descending order of the full sort key, so among entries that tie, the one that
    /// came first in the source sequence is the one returned. Entries that share a calendar day but
    /// differ in time of day are ordered by the time component too (the latest wins), which is the
    /// old behaviour and not what the forward cursor in <c>GetNetWorthHistoryCoreAsync</c> does
    /// (last entry of that day) — the two only disagree on same-day duplicates, and are
    /// deliberately left as they were.
    /// </summary>
    internal sealed class AsOfPriceSeries<TKey>
    {
        private readonly TKey[] _keys;      // descending, ties in source order
        private readonly decimal[] _closes; // parallel to _keys
        private readonly IComparer<TKey> _comparer;

        internal AsOfPriceSeries(TKey[] keysDescending, decimal[] closes, IComparer<TKey> comparer)
        {
            _keys = keysDescending;
            _closes = closes;
            _comparer = comparer;
        }

        public int Count => _keys.Length;

        /// <summary>
        /// Close of the latest entry whose day is on or before <paramref name="day"/>, or null when
        /// the series has nothing that early (callers differ on what "none" means, so it is not
        /// folded into 0 here).
        /// </summary>
        public decimal? CloseOnOrBefore(TKey day)
        {
            // First index whose key is <= day. The keys are descending, so "<= day" is false for a
            // prefix and true from there on — a monotonic predicate, which is what makes this valid.
            int lo = 0, hi = _keys.Length;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (_comparer.Compare(_keys[mid], day) <= 0) hi = mid;
                else lo = mid + 1;
            }
            return lo < _keys.Length ? _closes[lo] : null;
        }
    }

    internal static class AsOfPriceSeries
    {
        /// <summary>Stock history keyed by calendar day; ordered by the full <c>Date</c> (time of day included).</summary>
        public static AsOfPriceSeries<DateOnly> ForStock(IEnumerable<DailyPrice> prices)
        {
            var sorted = prices.OrderByDescending(x => x.Date).ToArray();
            return new AsOfPriceSeries<DateOnly>(
                [.. sorted.Select(x => DateOnly.FromDateTime(x.Date))],
                [.. sorted.Select(x => x.Close)],
                Comparer<DateOnly>.Default);
        }

        /// <summary>Fund NAV history keyed by its ISO <c>yyyy-MM-dd</c> date string, compared ordinally.</summary>
        public static AsOfPriceSeries<string> ForFund(IEnumerable<PricePoint> prices)
        {
            var sorted = prices.OrderByDescending(x => x.Date, StringComparer.Ordinal).ToArray();
            return new AsOfPriceSeries<string>(
                [.. sorted.Select(x => x.Date)],
                [.. sorted.Select(x => x.Close)],
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The as-of series for every symbol a valuation loop can ask about, built once per request
    /// from the fetched price maps. A symbol absent from a map is absent here, so callers keep
    /// their existing "no data for this symbol" branch.
    /// </summary>
    internal sealed class AsOfPriceBook
    {
        public IReadOnlyDictionary<string, AsOfPriceSeries<DateOnly>> Stocks { get; }

        /// <summary>Per fund: the NAV currency (the series itself carries none) and its NAV series.</summary>
        public IReadOnlyDictionary<string, (string Currency, AsOfPriceSeries<string> Prices)> Funds { get; }

        private AsOfPriceBook(
            IReadOnlyDictionary<string, AsOfPriceSeries<DateOnly>> stocks,
            IReadOnlyDictionary<string, (string Currency, AsOfPriceSeries<string> Prices)> funds)
        {
            Stocks = stocks;
            Funds = funds;
        }

        public static AsOfPriceBook From(
            IReadOnlyDictionary<string, MarketData> stockMap,
            IReadOnlyDictionary<string, FundNavHistory> fundMap) =>
            new(
                stockMap.ToDictionary(kv => kv.Key, kv => AsOfPriceSeries.ForStock(kv.Value.HistoricalPrices)),
                fundMap.ToDictionary(kv => kv.Key, kv => (kv.Value.Currency, AsOfPriceSeries.ForFund(kv.Value.Prices))));
    }
}
