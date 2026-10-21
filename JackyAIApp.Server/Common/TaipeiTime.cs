namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Taipei time, the clock every TWSE rule runs on: UTC+8 all year — Taiwan has no daylight
    /// saving, so a fixed offset is exact. Read it through the injected <see cref="TimeProvider"/>
    /// (<c>time.GetTaipeiNow()</c>) so tests can pin the clock.
    /// </summary>
    public static class TaipeiTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

        /// <summary>The wall-clock time in Taipei at <paramref name="instant"/>.</summary>
        public static DateTime ToTaipei(DateTimeOffset instant) => instant.ToOffset(Offset).DateTime;

        public static DateTime GetTaipeiNow(this TimeProvider time) => ToTaipei(time.GetUtcNow());

        public static DateOnly GetTaipeiToday(this TimeProvider time) => DateOnly.FromDateTime(time.GetTaipeiNow());
    }
}
