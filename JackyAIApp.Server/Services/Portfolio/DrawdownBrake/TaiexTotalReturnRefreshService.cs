namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// Keeps <c>TaiexTotalReturnDaily</c> current from outside any request, in the manner of
    /// <see cref="TwListedCompanyRefreshService"/>: checks at startup and then hourly, and a
    /// failed round is simply tried again an hour later, so no bookkeeping is needed to survive
    /// restarts. Each check decides what to fetch from what is stored:
    ///
    /// <list type="bullet">
    /// <item><b>Forward</b>: on a weekday after 14:00 (Taipei) when the newest stored day is
    /// before today, fetch from that day's month up to the current one, which also fills any gap
    /// left by downtime.</item>
    /// <item><b>Backward</b>: while the stored history starts later than <see cref="BackfillYears"/>
    /// years back, fetch the months before it, newest first. This covers an empty table (the
    /// current month first, so the latest day is there from the first request) and a backfill
    /// interrupted by a failure, which simply continues. Newest-first is also what lets the
    /// endpoint say "initializing" for exactly as long as the history is too short to trust,
    /// rather than showing a signal for a stale date while an oldest-first backfill catches up.</item>
    /// </list>
    ///
    /// The 3-year backfill (rather than back to 2003) is about 37 requests at 3 seconds apiece, and
    /// is enough for the signal to reach the same state as a full-history replay.
    /// </summary>
    public class TaiexTotalReturnRefreshService(
        ITaiexTotalReturnClient client,
        ITaiexTotalReturnStore store,
        ILogger<TaiexTotalReturnRefreshService> logger,
        TimeProvider? timeProvider = null) : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

        internal const int BackfillYears = 3;

        /// <summary>From this time (Taipei) on a trading day, that day's close is taken as final.</summary>
        internal static readonly TimeSpan PublishedAfter = TimeSpan.FromHours(14);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckEvery, _time);
            do
            {
                await RefreshAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        internal async Task RefreshAsync(CancellationToken ct)
        {
            try
            {
                var taipeiNow = _time.GetUtcNow().UtcDateTime.AddHours(8); // Taiwan has no DST
                var range = await store.GetRangeAsync(ct);

                foreach (var (year, month) in PlanMonths(range, taipeiNow))
                {
                    var result = await client.FetchMonthAsync(year, month, ct);
                    if (!result.Success)
                    {
                        // Stop the round: the next attempt is an hour away, and continuing would
                        // only send more requests to a host that just refused us.
                        logger.LogWarning("TAIEX total-return refresh stopped at {Year}-{Month:00}; retrying in {Interval}", year, month, CheckEvery);
                        return;
                    }

                    var final = result.Points.Where(p => IsFinal(p.Date, taipeiNow)).ToList();
                    var changed = await store.UpsertAsync(final, ct);
                    logger.LogInformation("TAIEX total-return {Year}-{Month:00}: {Fetched} days fetched, {Changed} stored", year, month, final.Count, changed);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Never let one bad hour (database blip included) end the loop for the life of
                // the process.
                logger.LogWarning(ex, "TAIEX total-return refresh threw; retrying in {Interval}", CheckEvery);
            }
        }

        /// <summary>The months to fetch, in order, given what is stored and the Taipei time now.</summary>
        internal static IReadOnlyList<(int Year, int Month)> PlanMonths(TaiexStoredRange? range, DateTime taipeiNow)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            var current = (today.Year, today.Month);
            var windowStart = MonthOf(today.AddYears(-BackfillYears));

            if (range is not { } stored)
                return MonthsDescending(current, windowStart);

            var plan = new List<(int Year, int Month)>();

            if (IsWeekday(today) && taipeiNow.TimeOfDay >= PublishedAfter && stored.Latest < today)
                plan.AddRange(MonthsAscending(MonthOf(stored.Latest), current));

            var earliestMonth = MonthOf(stored.Earliest);
            if (earliestMonth.CompareTo(windowStart) > 0)
                plan.AddRange(MonthsDescending(Previous(earliestMonth), windowStart));

            return plan;
        }

        /// <summary>Whether a fetched day is a finished one: before today, or today once the close
        /// has been published. Today's number earlier than that must never be stored.</summary>
        internal static bool IsFinal(DateOnly date, DateTime taipeiNow)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            if (date < today) return true;
            return date == today && IsWeekday(today) && taipeiNow.TimeOfDay >= PublishedAfter;
        }

        private static bool IsWeekday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        private static (int Year, int Month) MonthOf(DateOnly d) => (d.Year, d.Month);

        private static (int Year, int Month) Previous((int Year, int Month) m) =>
            m.Month == 1 ? (m.Year - 1, 12) : (m.Year, m.Month - 1);

        private static (int Year, int Month) Next((int Year, int Month) m) =>
            m.Month == 12 ? (m.Year + 1, 1) : (m.Year, m.Month + 1);

        private static List<(int Year, int Month)> MonthsAscending((int Year, int Month) from, (int Year, int Month) to)
        {
            var months = new List<(int, int)>();
            for (var m = from; m.CompareTo(to) <= 0; m = Next(m)) months.Add(m);
            return months;
        }

        private static List<(int Year, int Month)> MonthsDescending((int Year, int Month) from, (int Year, int Month) to)
        {
            var months = new List<(int, int)>();
            for (var m = from; m.CompareTo(to) >= 0; m = Previous(m)) months.Add(m);
            return months;
        }
    }
}
