namespace JackyAIApp.Server.Tests.TestHelpers
{
    /// <summary>A clock that reads <see cref="Now"/>, which a test may move.</summary>
    public sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        /// <summary>The clock at hour:minute on <paramref name="day"/> in Taipei.</summary>
        public static DateTimeOffset Taipei(DateOnly day, int hour, int minute = 0) =>
            new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(8));
    }
}
