namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>How a manual refresh ended. Serialized by name, so the member names are the API
    /// contract (TASK-041); the second phase may add members.</summary>
    public enum DrawdownBrakeRefreshResult
    {
        /// <summary>TWSE answered and at least one new or changed day was stored.</summary>
        Updated,

        /// <summary>Nothing newer to be had: either it cannot exist yet (no request was sent), or
        /// TWSE answered and has not published it.</summary>
        NoNewData,

        /// <summary>TWSE did not give a readable answer, so nothing from that month was stored.</summary>
        SourceUnavailable,

        /// <summary>Someone (the background round included) asked TWSE too recently; no request was sent.</summary>
        Throttled,
    }

    /// <param name="Result">How the refresh ended.</param>
    /// <param name="RetryAfterSeconds">Whole seconds until it may be tried again; only for <see cref="DrawdownBrakeRefreshResult.Throttled"/>.</param>
    public readonly record struct DrawdownBrakeRefreshOutcome(DrawdownBrakeRefreshResult Result, int? RetryAfterSeconds = null);

    public interface ITaiexTotalReturnRefresher
    {
        /// <summary>
        /// A user-triggered catch-up of the newest days. At most one request round per cooldown
        /// across the whole site, so the load on TWSE does not depend on how many people press the
        /// button. Never backfills; see <see cref="TaiexTotalReturnRefreshService"/>.
        /// </summary>
        Task<DrawdownBrakeRefreshOutcome> TryRefreshNowAsync(CancellationToken ct = default);
    }

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
    ///
    /// <para>It is also what a user's "refresh" button reaches (<see cref="TryRefreshNowAsync"/>),
    /// and the two share one gate and one cooldown: only one round runs at a time, and a manual
    /// refresh is answered <see cref="DrawdownBrakeRefreshResult.Throttled"/> for
    /// <see cref="Cooldown"/> after any round that sent TWSE a request, background or manual. The
    /// cooldown is what keeps TWSE's load independent of the number of users. It binds only the
    /// manual side: the hourly round still runs when its tick comes (waiting its turn behind a
    /// manual one), since a failed manual attempt is exactly when a retry is wanted, and an
    /// hourly request is no load.</para>
    /// </summary>
    public class TaiexTotalReturnRefreshService(
        ITaiexTotalReturnClient client,
        ITaiexTotalReturnStore store,
        ITaiexDataStatus status,
        ILogger<TaiexTotalReturnRefreshService> logger,
        TimeProvider? timeProvider = null) : BackgroundService, ITaiexTotalReturnRefresher
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

        internal const int BackfillYears = 3;

        /// <summary>From this time (Taipei) on a trading day, that day's close is taken as final.</summary>
        internal static readonly TimeSpan PublishedAfter = TimeSpan.FromHours(14);

        /// <summary>Site-wide quiet time after a round that sent TWSE a request, counted from when
        /// that round ended.</summary>
        internal static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>One round at a time, background or manual. Also guards <see cref="_lastRoundEndedAt"/>.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>When the last round that sent a request ended; null if none has since start-up.
        /// Read and written only while holding <see cref="_gate"/>.</summary>
        private DateTimeOffset? _lastRoundEndedAt;

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
                await _gate.WaitAsync(ct);
                try
                {
                    var taipeiNow = TaipeiNow();
                    var range = await store.GetRangeAsync(ct);

                    var plan = PlanMonths(range, taipeiNow);
                    var round = await FetchAsync(plan, taipeiNow, ct);
                    if (round.FailedAt is { } failed)
                    {
                        // Stop the round: the next attempt is an hour away, and continuing would
                        // only send more requests to a host that just refused us.
                        logger.LogWarning("TAIEX total-return refresh stopped at {Year}-{Month:00}; retrying in {Interval}", failed.Year, failed.Month, CheckEvery);
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Never let one bad hour (database blip included) end the loop for the life of
                // the process.
                logger.LogWarning(ex, "TAIEX total-return refresh threw; retrying in {Interval}", CheckEvery);
            }
        }

        public async Task<DrawdownBrakeRefreshOutcome> TryRefreshNowAsync(CancellationToken ct = default)
        {
            var (outcome, contacted) = await TryRefreshCoreAsync(ct);
            logger.LogInformation(
                "Drawdown-brake manual refresh: {Result}, TWSE {Contacted}",
                outcome.Result, contacted ? "asked" : "not asked");
            return outcome;
        }

        private async Task<(DrawdownBrakeRefreshOutcome Outcome, bool Contacted)> TryRefreshCoreAsync(CancellationToken ct)
        {
            // A round already running (background or someone else's press) is as good as a cooldown
            // just begun: do not queue behind it, and do not start a second one beside it.
            if (!await _gate.WaitAsync(0, ct))
                return (Throttled(Cooldown), false);

            try
            {
                var now = _time.GetUtcNow();
                if (_lastRoundEndedAt is { } ended && now - ended < Cooldown)
                    return (Throttled(Cooldown - (now - ended)), false);

                var taipeiNow = TaipeiNow();
                var range = await store.GetRangeAsync(ct);

                // An empty table is the backfill's business (it is "initializing"), not this
                // button's: a manual refresh only catches up the newest days.
                if (range is not { } stored || NoNewDataPossible(stored.Latest, taipeiNow))
                    return (new DrawdownBrakeRefreshOutcome(DrawdownBrakeRefreshResult.NoNewData), false);

                var plan = MonthsAscending(MonthOf(stored.Latest), MonthOf(DateOnly.FromDateTime(taipeiNow)));
                var round = await FetchAsync(plan, taipeiNow, ct);

                var result = round.FailedAt is not null ? DrawdownBrakeRefreshResult.SourceUnavailable
                    : round.Changed > 0 ? DrawdownBrakeRefreshResult.Updated
                    : DrawdownBrakeRefreshResult.NoNewData;
                return (new DrawdownBrakeRefreshOutcome(result), true);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The outcome of one pass over a plan: how many rows were added or changed, and
        /// the month that stopped it, if any.</summary>
        private readonly record struct Round(int Changed, (int Year, int Month)? FailedAt);

        /// <summary>
        /// Fetches and stores <paramref name="plan"/> in order, stopping at the first month TWSE
        /// does not answer readably. Call it holding <see cref="_gate"/>: it also starts the
        /// cooldown, if it sent a request, whether it finishes or throws.
        /// </summary>
        private async Task<Round> FetchAsync(IReadOnlyList<(int Year, int Month)> plan, DateTime taipeiNow, CancellationToken ct)
        {
            var changedTotal = 0;
            try
            {
                foreach (var (year, month) in plan)
                {
                    var result = await client.FetchMonthAsync(year, month, ct);
                    if (!result.Success) return new Round(changedTotal, (year, month));

                    var final = result.Points.Where(p => IsFinal(p.Date, taipeiNow)).ToList();
                    var changed = await store.UpsertAsync(final, ct);
                    // After the upsert, so a signal rebuilt on seeing the new version includes these days.
                    status.RecordCheck(_time.GetUtcNow());
                    changedTotal += changed;
                    logger.LogInformation("TAIEX total-return {Year}-{Month:00}: {Fetched} days fetched, {Changed} stored", year, month, final.Count, changed);
                }
                return new Round(changedTotal, null);
            }
            finally
            {
                if (plan.Count > 0) _lastRoundEndedAt = _time.GetUtcNow();
            }
        }

        private DateTime TaipeiNow() => _time.GetUtcNow().UtcDateTime.AddHours(8); // Taiwan has no DST

        private static DrawdownBrakeRefreshOutcome Throttled(TimeSpan remaining) =>
            new(DrawdownBrakeRefreshResult.Throttled, Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)));

        /// <summary>
        /// Whether asking TWSE now cannot turn up anything newer than <paramref name="latest"/>:
        /// the newest close that can be final yet is today's on a weekday from 14:00, and otherwise
        /// the previous weekday's. So a weekend, or a weekday before the close is published, is
        /// quiet exactly when the newest stored day is already that one. (Market holidays are not
        /// known here, so a holiday still sends a request — bounded by the cooldown.)
        /// </summary>
        internal static bool NoNewDataPossible(DateOnly latest, DateTime taipeiNow)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            DateOnly newestPossible;
            if (IsWeekday(today) && taipeiNow.TimeOfDay >= PublishedAfter)
            {
                newestPossible = today;
            }
            else
            {
                newestPossible = today.AddDays(-1);
                while (!IsWeekday(newestPossible)) newestPossible = newestPossible.AddDays(-1);
            }
            return latest >= newestPossible;
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
