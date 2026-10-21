using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Market;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    public interface IDrawdownBrakeService
    {
        /// <summary>The current 回落煞車 signal, replayed from the stored index series. One copy is
        /// shared by every caller for up to a minute; new data ends that early.</summary>
        Task<DrawdownBrakeDto> GetAsync(CancellationToken ct = default);

        /// <summary><see cref="GetAsync"/>, and whether this call was handed a copy someone else had
        /// built. What the view metric is told; the refresh endpoint, which is not a view, uses
        /// <see cref="GetAsync"/>.</summary>
        Task<DrawdownBrakeLookup> GetWithCacheStatusAsync(CancellationToken ct = default);

        /// <summary><see cref="GetAsync"/> after asking MIS for a newer intraday estimate, if one is
        /// wanted now and the shared reading is more than
        /// <see cref="CacheTtl.Portfolio.DrawdownBrakeEstimateRefresh"/> old. What the refresh
        /// endpoint answers with; an estimate MIS could not give is just absent from it.</summary>
        Task<DrawdownBrakeDto> GetAfterEstimateRefreshAsync(CancellationToken ct = default);
    }

    /// <param name="Signal">The shared signal. Do not modify it.</param>
    /// <param name="CacheHit">True if the caller got a copy it did not build; false if it was the
    /// one that read the table and recomputed (a caller that waited for another's build is a hit:
    /// it cost the database nothing).</param>
    public readonly record struct DrawdownBrakeLookup(DrawdownBrakeDto Signal, bool CacheHit);

    /// <summary>
    /// Builds <see cref="DrawdownBrakeDto"/> from the stored series: runs
    /// <see cref="DrawdownBrakeCalculator"/> over all of it (it keeps no state of its own) and adds
    /// the two data-health flags, <c>isInitializing</c> and <c>isStale</c>.
    ///
    /// <para>The result is the same for everyone, so it is built once and kept for
    /// <see cref="CacheTtl.Portfolio.DrawdownBrakeSignal"/>, with concurrent callers waiting on
    /// that one build: reading the whole table costs the database once a minute, not once per
    /// request. A copy is also discarded the moment <see cref="ITaiexDataStatus.Version"/> moves
    /// on (the refresh stored days or heard from TWSE), so "just refreshed" never shows the old
    /// signal. Callers get the cached instance itself and must not modify it.</para>
    ///
    /// <para>On a trading day from 09:00 until that day's official close is stored, the signal also
    /// carries an intraday <see cref="DrawdownBrakeDto.Estimate"/> (TASK-038), from the one MIS
    /// reading the site shares (<see cref="IMisTaiexQuoteSource"/>), and once the close is in, says
    /// whether it overturned the last estimate (<see cref="DrawdownBrakeDto.EstimateCorrected"/>).
    /// Those ride on top of the official signal rather than inside its cache entry, so a new
    /// reading is shown at once instead of after the signal's own minute, and the official
    /// signal is never rebuilt, or altered in any way, because MIS is slow or down. (A slow MIS can
    /// still make a look wait: the one that has to ask again waits, with whoever arrives meanwhile,
    /// for at most <see cref="MisTaiexClient.DefaultTimeout"/>.)</para>
    /// </summary>
    public class DrawdownBrakeService(
        ITaiexTotalReturnStore store,
        ITradingCalendar calendar,
        ITaiexDataStatus status,
        IMisTaiexQuoteSource quotes,
        IMemoryCache cache,
        DrawdownBrakeTelemetry telemetry,
        TimeProvider? timeProvider = null) : IDrawdownBrakeService
    {
        internal const string CacheKey = "drawdownbrake:signal";

        /// <summary>Where the state of a trading day's last estimate is kept (<see cref="RecordEstimate"/>).</summary>
        internal static string EstimateRecordKey(DateOnly tradeDate) =>
            $"drawdownbrake:estimate:{tradeDate:yyyy-MM-dd}";

        private sealed record CachedSignal(DrawdownBrakeDto Signal, long Version, IReadOnlyList<IndexClose> Closes);

        /// <summary>The signal with its estimate laid on, remembered so everyone who sees the same
        /// official copy and the same MIS reading is handed the same object.</summary>
        private sealed record Composed(CachedSignal Base, MisQuoteSnapshot? Snapshot, bool Corrected, DrawdownBrakeDto Result);

        /// <summary>Less history than this and the signal is not trustworthy yet (Issue #431 §1:
        /// "at least 2 years").</summary>
        internal const int MinHistoryDays = 730;

        /// <summary>From this time (Taipei) on a trading day its data is overdue.</summary>
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(17);

        /// <summary>From this time (Taipei) on a trading day the index has a level to estimate from.</summary>
        internal static readonly TimeSpan EstimateFrom = TimeSpan.FromHours(9);

        private const decimal TriggerPct = DrawdownBrakeCalculator.DdTrigger * 100;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>Held by the one caller building the signal. Not a per-caller token's business:
        /// a caller that gives up simply lets the next one build it.</summary>
        private readonly SemaphoreSlim _build = new(1, 1);

        private volatile Composed? _composed;

        public async Task<DrawdownBrakeDto> GetAsync(CancellationToken ct = default) =>
            (await GetWithCacheStatusAsync(ct)).Signal;

        public async Task<DrawdownBrakeLookup> GetWithCacheStatusAsync(CancellationToken ct = default)
        {
            var (entry, cacheHit) = await GetOfficialAsync(ct);
            var signal = await WithEstimateAsync(entry, CacheTtl.Portfolio.DrawdownBrakeEstimateQuote, ct);
            return new(signal, cacheHit);
        }

        public async Task<DrawdownBrakeDto> GetAfterEstimateRefreshAsync(CancellationToken ct = default)
        {
            var (entry, _) = await GetOfficialAsync(ct);
            return await WithEstimateAsync(entry, CacheTtl.Portfolio.DrawdownBrakeEstimateRefresh, ct);
        }

        private async Task<(CachedSignal Entry, bool CacheHit)> GetOfficialAsync(CancellationToken ct)
        {
            if (TryGetFresh() is { } hit) return (hit, true);

            await _build.WaitAsync(ct);
            try
            {
                // Whoever held the lock may have just built it.
                if (TryGetFresh() is { } built) return (built, true);

                // Read before anything below: if the data moves while this is being built, the copy
                // is tagged with the older version and is rebuilt on the next call.
                var version = status.Version;
                var started = _time.GetTimestamp();
                var (signal, closes) = await BuildAsync(ct);
                telemetry.RecordCompute(_time.GetElapsedTime(started));
                var entry = new CachedSignal(signal, version, closes);
                cache.Set(CacheKey, entry, CacheTtl.Portfolio.DrawdownBrakeSignal);
                return (entry, false);
            }
            finally
            {
                _build.Release();
            }
        }

        private CachedSignal? TryGetFresh()
        {
            if (!cache.TryGetValue(CacheKey, out CachedSignal? entry) || entry is null) return null;
            if (entry.Version != status.Version) return null;
            // The cache entry expires on the same TTL by the system clock; this is the same bound
            // by this service's clock, which is what lets a test move time.
            if (_time.GetUtcNow() - entry.Signal.GeneratedAt >= CacheTtl.Portfolio.DrawdownBrakeSignal) return null;
            return entry;
        }

        /// <summary>
        /// The official signal with the estimate and the "corrected" flag laid on, when there is
        /// anything to lay on; otherwise the shared official copy itself. MIS is asked, through the
        /// shared reading, only while an estimate is wanted (<see cref="EstimateWanted"/>).
        /// </summary>
        private async Task<DrawdownBrakeDto> WithEstimateAsync(CachedSignal entry, TimeSpan quoteMaxAge, CancellationToken ct)
        {
            var official = entry.Signal;
            // Initializing: no official day, so nothing to scale an estimate from or to overturn.
            if (official.AsOfDate is not { } asOf || official.State is not { } state) return official;

            var taipeiNow = _time.GetTaipeiNow();
            var today = DateOnly.FromDateTime(taipeiNow);
            var corrected = WasCorrected(asOf, state);

            MisQuoteSnapshot? snapshot = null;
            if (EstimateWanted(asOf, today, taipeiNow))
                snapshot = await quotes.GetAsync(quoteMaxAge, ct);

            // A reading of another day (MIS answers a market holiday with the last session's) is no
            // estimate of today.
            var quote = snapshot?.Quote is { } q && q.TradeDate == today ? q : null;
            if (quote is null && !corrected) return official;

            if (_composed is { } same && ReferenceEquals(same.Base, entry)
                && ReferenceEquals(same.Snapshot, snapshot) && same.Corrected == corrected)
                return same.Result;

            DrawdownBrakeEstimateDto? estimate = null;
            if (quote is not null)
            {
                estimate = DrawdownBrakeEstimator.Estimate(entry.Closes, quote);
                RecordEstimate(today, estimate.State);
            }

            var result = official.WithEstimate(estimate, corrected);
            _composed = new Composed(entry, snapshot, corrected, result);
            return result;
        }

        /// <summary>
        /// Whether an estimate is wanted now: a weekday from <see cref="EstimateFrom"/> Taipei time, while
        /// the day's official close is not stored yet. The estimate scales the stored close by the day's
        /// move, so that close must be the previous trading day's: any weekday between it and today
        /// has to be a known market holiday, and where the calendar cannot say, no estimate is made
        /// rather than one from a stale base. (A holiday today needs no check: MIS then answers with the
        /// last session's date, which <see cref="WithEstimateAsync"/> turns away.)
        /// </summary>
        private bool EstimateWanted(DateOnly asOf, DateOnly today, DateTime taipeiNow)
        {
            if (asOf >= today) return false;
            if (IsWeekend(today)) return false;
            if (taipeiNow.TimeOfDay < EstimateFrom) return false;

            for (var d = asOf.AddDays(1); d < today; d = d.AddDays(1))
                if (!IsWeekend(d) && calendar.IsMarketClosed(d) != true)
                    return false;
            return true;
        }

        private static bool IsWeekend(DateOnly d) => WeekdayTradingCalendar.IsWeekend(d);

        /// <summary>Remembers the state of the latest estimate of <paramref name="tradeDate"/>: what
        /// the official close will be compared with. In memory only; a restart forgets it, and the
        /// comparison then simply says nothing.</summary>
        private void RecordEstimate(DateOnly tradeDate, BrakeState state) =>
            cache.Set(EstimateRecordKey(tradeDate), state, CacheTtl.Portfolio.DrawdownBrakeEstimateRecord);

        /// <summary>The last estimate of <paramref name="asOf"/> (the official day now being shown) named
        /// a different state from the official one.</summary>
        private bool WasCorrected(DateOnly asOf, BrakeState official) =>
            cache.TryGetValue(EstimateRecordKey(asOf), out BrakeState estimated) && estimated != official;

        private async Task<(DrawdownBrakeDto Signal, IReadOnlyList<IndexClose> Closes)> BuildAsync(CancellationToken ct)
        {
            var generatedAt = _time.GetUtcNow();
            var lastCheckedAt = status.LastCheckedAt;

            var closes = await store.GetAllAsync(ct);
            if (closes.Count == 0 || closes[^1].Date.DayNumber - closes[0].Date.DayNumber < MinHistoryDays)
                return (new DrawdownBrakeDto { IsInitializing = true, LastCheckedAt = lastCheckedAt, GeneratedAt = generatedAt }, closes);

            var days = DrawdownBrakeCalculator.Compute(closes);
            var last = days[^1];
            var drawdownPct = last.Drawdown * 100;

            var taipeiNow = TaipeiTime.ToTaipei(generatedAt);
            var signal = new DrawdownBrakeDto
            {
                AsOfDate = last.Date,
                State = last.State,
                PrevState = days.Count > 1 ? days[^2].State : null,
                LastSwitchDate = LastSwitchDate(days),
                Close = last.Close,
                High252 = last.High252,
                Sma60 = last.Sma60 is { } sma ? Math.Round(sma, 4) : null,
                DrawdownPct = Math.Round(drawdownPct, 4),
                DistanceToTriggerPct = Math.Round(drawdownPct + TriggerPct, 4),
                IsStale = IsStale(last.Date, taipeiNow),
                LastCheckedAt = lastCheckedAt,
                GeneratedAt = generatedAt,
            };
            return (signal, closes);
        }

        /// <summary>The first day of the current run of equal states; null if there has been no switch.</summary>
        internal static DateOnly? LastSwitchDate(IReadOnlyList<BrakeDay> days)
        {
            for (var i = days.Count - 1; i > 0; i--)
                if (days[i].State != days[i - 1].State) return days[i].Date;
            return null;
        }

        /// <summary>
        /// Late data: a weekday, after 17:00 Taipei, with today's close still missing — unless the
        /// market was shut today. Where the holiday calendar cannot answer, falls back to
        /// requiring two or more missing weekdays in a row, so a single closed day does not raise
        /// a warning (and a long break, like Lunar New Year, may).
        /// </summary>
        internal bool IsStale(DateOnly latest, DateTime taipeiNow)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            if (IsWeekend(today)) return false;
            if (taipeiNow.TimeOfDay < StaleAfter) return false;
            if (latest >= today) return false;

            return calendar.IsMarketClosed(today) switch
            {
                true => false,
                false => true,
                null => MissingWeekdays(latest, today) >= 2,
            };
        }

        /// <summary>Weekdays after <paramref name="latest"/>, up to and including <paramref name="today"/>.</summary>
        private static int MissingWeekdays(DateOnly latest, DateOnly today)
        {
            var count = 0;
            for (var d = latest.AddDays(1); d <= today; d = d.AddDays(1))
                if (!IsWeekend(d)) count++;
            return count;
        }
    }
}
