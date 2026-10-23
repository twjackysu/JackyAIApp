namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// One benchmark in the comparison: what the user's contributions would have bought of it instead. Each
    /// contribution buys units at that day's price (a withdrawal sells, never below none), and a dividend is
    /// income on the units held going into its ex-date. Benchmarks never affect each other.
    /// </summary>
    internal sealed class BenchmarkLeg(
        string symbol, ForwardPriceCursor<DateOnly> prices, IReadOnlyDictionary<DateOnly, decimal> dividendPerUnitByDate)
    {
        private decimal _units;
        private decimal _pendingIncome;

        public string Symbol => symbol;

        /// <summary>Its value on each recorded day: the plotted curve, pure market value.</summary>
        public List<decimal> Values { get; } = [];

        /// <summary>Its 含息 time-weighted return over the recorded days.</summary>
        public TimeWeightedReturn Return { get; } = new();

        /// <summary>The last close on or before <paramref name="day"/>, 0 without one; days in non-decreasing order.</summary>
        public decimal PriceOn(DateOnly day) => prices.CloseOnOrBefore(day) ?? 0;

        /// <summary>What was already invested when the window starts, bought at its start price.</summary>
        public void Seed(decimal invested, DateOnly startDate)
        {
            var price = PriceOn(startDate);
            if (price > 0) _units = invested / price;
        }

        public void AccrueDividends(DateOnly day) =>
            _pendingIncome += _units * dividendPerUnitByDate.GetValueOrDefault(day);

        public void Invest(decimal amount, DateOnly day)
        {
            var price = PriceOn(day);
            if (price > 0) _units = Math.Max(0, _units + amount / price);
        }

        /// <summary>A day that counts: its value onto the curve, its growth into the return.</summary>
        public void Record(DateOnly day, decimal contribution)
        {
            var value = _units * PriceOn(day);
            Values.Add(Math.Round(value, 2));
            Return.Record(value, contribution, _pendingIncome);
            _pendingIncome = 0;
        }
    }
}
