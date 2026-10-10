namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>The 回落煞車 market state. Serialized by name, so the member names are the API contract.</summary>
    public enum BrakeState { OFFENSE, DEFENSE }

    /// <summary>One day's close in the series the brake is computed from.</summary>
    public readonly record struct IndexClose(DateOnly Date, decimal Close);

    /// <summary>One day of the recomputed series. <see cref="Sma60"/> is null until 60 closes exist;
    /// <see cref="Drawdown"/> is a ratio (−0.012 = 1.2% below the 252-day high).</summary>
    public readonly record struct BrakeDay(
        DateOnly Date, decimal Close, decimal High252, decimal? Sma60, decimal Drawdown, BrakeState State);

    /// <summary>
    /// The 回落煞車 state machine from Issue #431 §1, as a pure function of the close series: no
    /// database, clock or stored state. Every call replays the whole series from the initial
    /// OFFENSE state, so the answer never depends on a previously persisted state.
    ///
    /// The three parameters are the Boss's backtested values (DECISION-013) and are constants on
    /// purpose — they are not settings, and nobody may tune them (60 → 80 days measurably hurts).
    /// </summary>
    public static class DrawdownBrakeCalculator
    {
        public const decimal DdTrigger = 0.08m;
        public const int ReenterSma = 60;
        public const int LookbackHigh = 252;

        /// <summary>
        /// Replays <paramref name="closes"/> (ascending by date, one close per trading day) and
        /// returns the state after every day. Empty input gives an empty result.
        /// </summary>
        public static IReadOnlyList<BrakeDay> Compute(IReadOnlyList<IndexClose> closes)
        {
            var days = new BrakeDay[closes.Count];
            var state = BrakeState.OFFENSE;
            decimal smaSum = 0;

            for (var i = 0; i < closes.Count; i++)
            {
                var close = closes[i].Close;

                // Rolling mean of the last 60 closes, today included. Decimal addition is exact,
                // so the running sum cannot drift the way a float one would.
                smaSum += close;
                if (i >= ReenterSma) smaSum -= closes[i - ReenterSma].Close;
                decimal? sma60 = i + 1 >= ReenterSma ? smaSum / ReenterSma : null;

                // Highest close of the last 252 days, today included; with fewer than 252 days
                // of history it is the highest so far (pandas min_periods=1 in the reference).
                var high252 = close;
                for (var j = Math.Max(0, i - LookbackHigh + 1); j < i; j++)
                    if (closes[j].Close > high252) high252 = closes[j].Close;

                var drawdown = close / high252 - 1;

                var up = sma60 is { } sma && close > sma;
                var down = drawdown < -DdTrigger; // strictly: exactly −8.0% is not a trigger

                // up wins when both hold; when neither does, yesterday's state stands.
                if (up) state = BrakeState.OFFENSE;
                else if (down) state = BrakeState.DEFENSE;

                days[i] = new BrakeDay(closes[i].Date, close, high252, sma60, drawdown, state);
            }

            return days;
        }
    }
}
