using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// "The last close on or before day D" for days asked in non-decreasing order, walking a sorted copy of
    /// one instrument's history forward instead of searching it for every day of a loop. Among entries of
    /// one day, the last in date-and-time order wins — <see cref="AsOfPriceSeries{TKey}"/> differs on exact
    /// ties, and the screens using each are deliberately left as they were.
    /// </summary>
    /// <remarks>Built per request from a copy: the price lists are cached, shared instances, never reordered in place.</remarks>
    internal sealed class ForwardPriceCursor<TKey>
    {
        private readonly TKey[] _days;      // ascending
        private readonly decimal[] _closes; // parallel to _days
        private readonly IComparer<TKey> _comparer;
        private int _index;

        internal ForwardPriceCursor(TKey[] daysAscending, decimal[] closes, IComparer<TKey> comparer)
        {
            _days = daysAscending;
            _closes = closes;
            _comparer = comparer;
        }

        /// <summary>The close, or null when the history has nothing that early (or nothing at all).</summary>
        public decimal? CloseOnOrBefore(TKey day)
        {
            if (_days.Length == 0) return null;
            while (_index + 1 < _days.Length && _comparer.Compare(_days[_index + 1], day) <= 0)
                _index++;
            return _comparer.Compare(_days[_index], day) <= 0 ? _closes[_index] : null;
        }
    }

    internal static class ForwardPriceCursor
    {
        /// <summary>A stock's history, by calendar day.</summary>
        public static ForwardPriceCursor<DateOnly> ForStock(IEnumerable<DailyPrice> prices)
        {
            var sorted = prices.OrderBy(x => x.Date).ToArray();
            return new ForwardPriceCursor<DateOnly>(
                [.. sorted.Select(x => DateOnly.FromDateTime(x.Date))], [.. sorted.Select(x => x.Close)], Comparer<DateOnly>.Default);
        }

        /// <summary>A fund's NAV history, by its ISO <c>yyyy-MM-dd</c> date compared ordinally (which is chronological).</summary>
        public static ForwardPriceCursor<string> ForFund(IEnumerable<PricePoint> prices)
        {
            var sorted = prices.OrderBy(x => x.Date, StringComparer.Ordinal).ToArray();
            return new ForwardPriceCursor<string>(
                [.. sorted.Select(x => x.Date)], [.. sorted.Select(x => x.Close)], StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The snapshot in effect on each day — the latest dated on or before it — for days asked in
    /// non-decreasing order; null before the first.
    /// </summary>
    internal sealed class SnapshotCursor(IReadOnlyList<HoldingSnapshot> snapshotsByDate)
    {
        private int _index;

        public HoldingSnapshot? ActiveOn(DateOnly day)
        {
            if (snapshotsByDate.Count == 0) return null;
            while (_index + 1 < snapshotsByDate.Count && snapshotsByDate[_index + 1].EffectiveDate <= day)
                _index++;
            return snapshotsByDate[_index].EffectiveDate <= day ? snapshotsByDate[_index] : null;
        }
    }
}
