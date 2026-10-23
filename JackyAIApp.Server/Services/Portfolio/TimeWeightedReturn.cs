namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// A time-weighted return, chained one recorded day at a time. A plain (last − first) ÷ first counts
    /// deposits as gains — an account that put 2.7M into a 7M portfolio reads +31% having earned nothing —
    /// so each day's growth is measured against the previous value plus that day's contribution. Income
    /// (a dividend) counts in its day's growth and then leaves the base: it is cash in hand, not reinvested,
    /// so it is credited exactly once.
    /// </summary>
    internal sealed class TimeWeightedReturn
    {
        private decimal _growth = 1m;
        private decimal? _previousValue;

        /// <param name="value">The day's value, after its contribution.</param>
        /// <param name="contribution">Money put in that day (negative when taken out).</param>
        /// <param name="income">Income earned since the last recorded day.</param>
        public void Record(decimal value, decimal contribution, decimal income)
        {
            var start = (_previousValue ?? 0) + contribution;
            // A base at or below zero has no meaningful growth: the day is skipped rather than divided by
            // zero or flipped in sign.
            if (_previousValue != null && start > 0)
                _growth *= (value + income) / start;
            _previousValue = value;
        }

        public decimal Pct => Math.Round((_growth - 1m) * 100, 2);
    }
}
