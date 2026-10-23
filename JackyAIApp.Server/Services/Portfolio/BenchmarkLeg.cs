namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// One benchmark in the comparison: what the user's contributions would have bought of it instead. Each
    /// contribution buys units at that day's price — or, when the benchmark has no price yet, at its first
    /// price after — a withdrawal sells (never below none), and a dividend is income on the units held going
    /// into its ex-date. Benchmarks never affect each other.
    /// </summary>
    internal sealed class BenchmarkLeg(
        string symbol, ForwardPriceCursor<DateOnly> prices, IReadOnlyDictionary<DateOnly, decimal> dividendPerUnitByDate)
    {
        private decimal _units;
        private decimal _pendingIncome;
        private decimal _toInvest;

        public string Symbol => symbol;

        /// <summary>Its value on each recorded day: the plotted curve, pure market value.</summary>
        public List<decimal> Values { get; } = [];

        /// <summary>Its 含息 time-weighted return over the recorded days.</summary>
        public TimeWeightedReturn Return { get; } = new();

        /// <summary>The last close on or before <paramref name="day"/>, 0 without one; days in non-decreasing order.</summary>
        public decimal PriceOn(DateOnly day) => prices.CloseOnOrBefore(day) ?? 0;

        /// <summary>What was already invested when the window starts, bought at its start price (or its first after).</summary>
        public void Seed(decimal invested, DateOnly startDate) => Invest(invested, startDate);

        public void AccrueDividends(DateOnly day) =>
            _pendingIncome += _units * dividendPerUnitByDate.GetValueOrDefault(day);

        public void Invest(decimal amount, DateOnly day)
        {
            _toInvest += amount;
            BuyWhatIsToInvest(day);
        }

        /// <summary>A day that counts: its value onto the curve, its growth into the return.</summary>
        public void Record(DateOnly day, decimal contribution)
        {
            BuyWhatIsToInvest(day);
            var value = _units * PriceOn(day);
            Values.Add(Math.Round(value, 2));
            Return.Record(value, contribution, _pendingIncome);
            _pendingIncome = 0;
        }

        private void BuyWhatIsToInvest(DateOnly day)
        {
            var price = PriceOn(day);
            if (_toInvest == 0 || price <= 0) return;
            _units = Math.Max(0, _units + _toInvest / price);
            _toInvest = 0;
        }
    }
}
