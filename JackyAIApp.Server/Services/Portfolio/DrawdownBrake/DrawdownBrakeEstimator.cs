using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// The intraday estimate of 回落煞車 (Issue #431 §5, TASK-038): where the signal would be if
    /// today closed at the index's current level. A pure function of the official series and one MIS
    /// reading; whether an estimate is wanted at all (the time window, the baseline being yesterday,
    /// the reading being today's) is the caller's business.
    ///
    /// <para>MIS gives only the price index, but the signal runs on the total-return index. So the
    /// estimate moves yesterday's official return-index close by the price index's move since its own
    /// previous close — <c>official × z ÷ y</c> — and then replays the whole series, today's point
    /// included, through <see cref="DrawdownBrakeCalculator"/>. That understates the day's return by the
    /// dividends going ex that day (mostly June to August); a state that lands near a threshold may
    /// differ from the official one, which is what the "signal corrected" flag is for. It is not
    /// adjusted for here (ARCHITECTURE.md, Trade-offs).</para>
    /// </summary>
    public static class DrawdownBrakeEstimator
    {
        /// <param name="official">The stored series, ascending; its last point is the day before the
        /// reading's. Must not be empty.</param>
        public static DrawdownBrakeEstimateDto Estimate(IReadOnlyList<IndexClose> official, MisIndexQuote quote)
        {
            if (official.Count == 0) throw new ArgumentException("An estimate needs at least one official close.", nameof(official));

            // The same four decimals the table keeps, so the estimate and a stored close compare alike.
            var estimated = Math.Round(official[^1].Close * quote.Last / quote.PreviousClose, 4);

            var series = new List<IndexClose>(official.Count + 1);
            series.AddRange(official);
            series.Add(new IndexClose(quote.TradeDate, estimated));

            var days = DrawdownBrakeCalculator.Compute(series);
            var today = days[^1];
            return new DrawdownBrakeEstimateDto
            {
                AsOfTime = quote.QuotedAt,
                EstimatedClose = estimated,
                State = today.State,
                PrevState = days[^2].State,
                DrawdownPct = Math.Round(today.Drawdown * 100, 4),
            };
        }
    }
}
