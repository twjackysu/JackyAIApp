using JackyAIApp.Server.DTO.Portfolio;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    public interface IDrawdownBrakeService
    {
        /// <summary>The current 回落煞車 signal, replayed from the stored index series.</summary>
        Task<DrawdownBrakeDto> GetAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Builds <see cref="DrawdownBrakeDto"/> from the stored series: runs
    /// <see cref="DrawdownBrakeCalculator"/> over all of it on every call (it keeps no state of its
    /// own) and adds the two data-health flags, <c>isInitializing</c> and <c>isStale</c>.
    /// </summary>
    public class DrawdownBrakeService(
        ITaiexTotalReturnStore store,
        ITwseHolidayCalendar calendar,
        TimeProvider? timeProvider = null) : IDrawdownBrakeService
    {
        /// <summary>Less history than this and the signal is not trustworthy yet (Issue #431 §1:
        /// "at least 2 years").</summary>
        internal const int MinHistoryDays = 730;

        /// <summary>From this time (Taipei) on a trading day its data is overdue.</summary>
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(17);

        private const decimal TriggerPct = DrawdownBrakeCalculator.DdTrigger * 100;

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<DrawdownBrakeDto> GetAsync(CancellationToken ct = default)
        {
            var closes = await store.GetAllAsync(ct);
            if (closes.Count == 0 || closes[^1].Date.DayNumber - closes[0].Date.DayNumber < MinHistoryDays)
                return new DrawdownBrakeDto { IsInitializing = true };

            var days = DrawdownBrakeCalculator.Compute(closes);
            var last = days[^1];
            var drawdownPct = last.Drawdown * 100;

            var taipeiNow = _time.GetUtcNow().UtcDateTime.AddHours(8); // Taiwan has no DST
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
