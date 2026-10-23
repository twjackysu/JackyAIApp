namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// What a read of TW history still has to get from the exchange, given what is stored
    /// (<see cref="PersistentTwHistoricalProvider"/>).
    /// </summary>
    internal static class TwGapFill
    {
        /// <summary>The quote fallback's cutoff, Taipei time — see <see cref="ShouldBackfillFromQuote"/>.</summary>
        private static readonly TimeSpan QuoteCutoff = new(8, 0, 0);

        /// <summary>
        /// The first day to fetch, or null when what is stored already answers the request. Only what is
        /// actually missing is asked for — the whole point of storing history is to stop paying the
        /// full-window cost on every cold start.
        /// <para>
        /// Forward: the stored bars must reach the last trading day before today. The DB only ever holds
        /// finalized bars (TradeDate &lt; today, see <c>UpsertFinalizedBarsAsync</c>) — today's own close is
        /// never stored, whenever the session closes: TWSE/TWStockLib publish no guarantee of exactly when
        /// today's bar is final, so the DB never gambles on it, and the app can show yesterday's close for
        /// a few hours after today's session closed, until the date rolls over (at midnight in Taipei).
        /// Holidays count: with the weekday rule the day after a weekday holiday took the holiday for the
        /// last trading day, found it missing, and fetched again on every request that day.
        /// </para>
        /// <para>
        /// Backward: only a deep request cares whether a deep fetch has ever run — e.g. a symbol added to
        /// a portfolio (shallow, ~10 days stored) that a chart later asks about deep. Without it the
        /// forward check alone would see the stored bars current and conclude "nothing to do", silently
        /// starving the deep caller of the six months it thinks it has. Keyed on an explicit "has this ever
        /// run" marker rather than the oldest stored row: a symbol with genuinely less history (a recent
        /// IPO) would otherwise look "not deep enough" forever and be fetched again on every call. A
        /// backward gap fetches the whole needed window, not just the forward gap; storing dedupes
        /// against existing rows, so an overlapping range is harmless.
        /// </para>
        /// </summary>
        /// <param name="deep">Whether the request wants the whole six months (a chart) or only the latest days.</param>
        /// <param name="neededFrom">The window this request needs: six months deep, ten days shallow.</param>
        /// <param name="storedThrough">The last stored day in the window, or null.</param>
        /// <param name="lastTradingDay">The last trading day before today.</param>
        /// <param name="deepFetchedBefore">Whether a deep fetch has ever run for the symbol.</param>
        public static DateOnly? Plan(
            bool deep, DateOnly neededFrom, DateOnly? storedThrough, DateOnly lastTradingDay, bool deepFetchedBefore)
        {
            if (deep && !deepFetchedBefore) return neededFrom;
            if (storedThrough is not { } last) return neededFrom;
            return last < lastTradingDay ? last.AddDays(1) : null;
        }

        /// <summary>
        /// Whether to fill the last trading day from the realtime quote's YesterdayClosingPrice. The fetch
        /// can still leave that one day missing — queried pre-market before TWSE's own STOCK_DAY endpoint
        /// has its row, or the fetch failed — and the quote is a second, independent source for exactly
        /// that day. Only before 08:00 Taipei: the fallback smooths over the pre-market window, and the
        /// server clock isn't guaranteed exact — an hour of margin before the 09:00 open means even a
        /// skewed clock can't take a still-forming session for yesterday's close. Past the cutoff a real
        /// gap is left for the next request's fetch.
        /// </summary>
        /// <param name="storedThrough">The last stored day after the fetch, or null.</param>
        /// <param name="lastTradingDay">The last trading day before today: the day the quote's yesterday is.</param>
        /// <param name="taipeiNow">The time in Taipei.</param>
        public static bool ShouldBackfillFromQuote(DateOnly? storedThrough, DateOnly lastTradingDay, DateTime taipeiNow) =>
            (storedThrough is null || storedThrough.Value < lastTradingDay) && taipeiNow.TimeOfDay < QuoteCutoff;
    }
}
