using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;
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
    /// </summary>
    public class DrawdownBrakeService(
        ITaiexTotalReturnStore store,
        ITwseHolidayCalendar calendar,
        ITaiexDataStatus status,
        IMemoryCache cache,
        DrawdownBrakeTelemetry telemetry,
        TimeProvider? timeProvider = null) : IDrawdownBrakeService
    {
        internal const string CacheKey = "drawdownbrake:signal";

        private sealed record CachedSignal(DrawdownBrakeDto Signal, long Version);

        /// <summary>Less history than this and the signal is not trustworthy yet (Issue #431 §1:
        /// "at least 2 years").</summary>
        internal const int MinHistoryDays = 730;

        /// <summary>From this time (Taipei) on a trading day its data is overdue.</summary>
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(17);

        private const decimal TriggerPct = DrawdownBrakeCalculator.DdTrigger * 100;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>Held by the one caller building the signal. Not a per-caller token's business:
        /// a caller that gives up simply lets the next one build it.</summary>
        private readonly SemaphoreSlim _build = new(1, 1);

        public async Task<DrawdownBrakeDto> GetAsync(CancellationToken ct = default) =>
            (await GetWithCacheStatusAsync(ct)).Signal;

        public async Task<DrawdownBrakeLookup> GetWithCacheStatusAsync(CancellationToken ct = default)
        {
            if (TryGetFresh() is { } hit) return new(hit, CacheHit: true);

            await _build.WaitAsync(ct);
            try
            {
                // Whoever held the lock may have just built it.
                if (TryGetFresh() is { } built) return new(built, CacheHit: true);

                // Read before anything below: if the data moves while this is being built, the copy
                // is tagged with the older version and is rebuilt on the next call.
                var version = status.Version;
                var started = _time.GetTimestamp();
                var signal = await BuildAsync(ct);
                telemetry.RecordCompute(_time.GetElapsedTime(started));
                cache.Set(CacheKey, new CachedSignal(signal, version), CacheTtl.Portfolio.DrawdownBrakeSignal);
                return new(signal, CacheHit: false);
            }
            finally
            {
                _build.Release();
            }
        }

        private DrawdownBrakeDto? TryGetFresh()
        {
            if (!cache.TryGetValue(CacheKey, out CachedSignal? entry) || entry is null) return null;
            if (entry.Version != status.Version) return null;
            // The cache entry expires on the same TTL by the system clock; this is the same bound
            // by this service's clock, which is what lets a test move time.
            if (_time.GetUtcNow() - entry.Signal.GeneratedAt >= CacheTtl.Portfolio.DrawdownBrakeSignal) return null;
            return entry.Signal;
        }

        private async Task<DrawdownBrakeDto> BuildAsync(CancellationToken ct)
        {
            var generatedAt = _time.GetUtcNow();
            var lastCheckedAt = status.LastCheckedAt;

            var closes = await store.GetAllAsync(ct);
            if (closes.Count == 0 || closes[^1].Date.DayNumber - closes[0].Date.DayNumber < MinHistoryDays)
                return new DrawdownBrakeDto { IsInitializing = true, LastCheckedAt = lastCheckedAt, GeneratedAt = generatedAt };

            var days = DrawdownBrakeCalculator.Compute(closes);
            var last = days[^1];
            var drawdownPct = last.Drawdown * 100;

            var taipeiNow = generatedAt.UtcDateTime.AddHours(8); // Taiwan has no DST
            return new DrawdownBrakeDto
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
                IsStale = await IsStaleAsync(last.Date, taipeiNow, ct),
                LastCheckedAt = lastCheckedAt,
                GeneratedAt = generatedAt,
            };
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
        internal async Task<bool> IsStaleAsync(DateOnly latest, DateTime taipeiNow, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(taipeiNow);
            if (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
            if (taipeiNow.TimeOfDay < StaleAfter) return false;
            if (latest >= today) return false;

            return await calendar.IsMarketClosedAsync(today, ct) switch
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
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
            return count;
        }
    }
}
