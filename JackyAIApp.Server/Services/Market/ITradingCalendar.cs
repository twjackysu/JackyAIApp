namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// Which days TWSE trades. Answers from memory, never by asking TWSE: the holiday schedule is
    /// loaded in the background (<see cref="TradingCalendarRefreshService"/>).
    /// </summary>
    public interface ITradingCalendar
    {
        /// <summary>
        /// True when the market is closed on <paramref name="date"/> (a weekend, or a day the holiday
        /// schedule lists), false when it trades, null when that cannot be told: a weekday of a year
        /// the schedule does not cover, or before it has loaded. Treat null as unknown, not as open —
        /// or use <see cref="TradingCalendarExtensions"/>, which count an unknown weekday as a trading day.
        /// </summary>
        bool? IsMarketClosed(DateOnly date);
    }

    public static class TradingCalendarExtensions
    {
        /// <summary>Whether the market trades on <paramref name="date"/>, counting an unknown weekday as
        /// a trading day — the weekday rule, which is what is left when the schedule cannot say.</summary>
        public static bool IsTradingDay(this ITradingCalendar calendar, DateOnly date) =>
            calendar.IsMarketClosed(date) != true;

        /// <summary>The last trading day before <paramref name="date"/>.</summary>
        public static DateOnly PreviousTradingDay(this ITradingCalendar calendar, DateOnly date)
        {
            var d = date.AddDays(-1);
            // The longest break (Lunar New Year with its weekends) is about ten days; a schedule that
            // closed more than a month in a row would be wrong, so stop rather than walk on.
            for (var i = 0; i < 31 && !calendar.IsTradingDay(d); i++)
                d = d.AddDays(-1);
            return d;
        }
    }

    /// <summary>The calendar with no holiday schedule: weekends are closed, every weekday is unknown
    /// (and so counted as a trading day). What code falls back to when it is given no calendar.</summary>
    public sealed class WeekdayTradingCalendar : ITradingCalendar
    {
        public static WeekdayTradingCalendar Instance { get; } = new();

        private WeekdayTradingCalendar() { }

        public bool? IsMarketClosed(DateOnly date) => IsWeekend(date) ? true : null;

        public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }
}
