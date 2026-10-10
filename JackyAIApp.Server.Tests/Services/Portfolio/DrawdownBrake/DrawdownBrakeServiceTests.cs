using System.Globalization;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class DrawdownBrakeServiceTests
    {
        private readonly Mock<ITaiexTotalReturnStore> _store = new();
        private readonly Mock<ITwseHolidayCalendar> _calendar = new();
        private readonly FakeTime _time = new();

        // Taipei = UTC+8.
        private static DateTimeOffset Taipei(int y, int m, int d, int h, int min = 0) =>
            new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.FromHours(8)).ToUniversalTime();

        private DrawdownBrakeService CreateService() => new(_store.Object, _calendar.Object, _time);

        private void StoreHolds(IReadOnlyList<IndexClose> closes) =>
            _store.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(closes);

        private static List<IndexClose> Flat(DateOnly start, int days, decimal close) =>
            Enumerable.Range(0, days).Select(i => new IndexClose(start.AddDays(i), close)).ToList();

        /// <summary>Consecutive calendar days ending on <paramref name="end"/>.</summary>
        private static List<IndexClose> FlatEndingOn(DateOnly end, int days, decimal close) =>
            Flat(end.AddDays(1 - days), days, close);

        // ── initializing ────────────────────────────────────────────────────

        [Fact]
        public async Task EmptyTable_IsInitializing_WithNoSignalFields()
        {
            StoreHolds([]);

            var dto = await CreateService().GetAsync();

            Assert.True(dto.IsInitializing);
            Assert.Null(dto.AsOfDate);
            Assert.Null(dto.State);
            Assert.Null(dto.PrevState);
            Assert.Null(dto.LastSwitchDate);
            Assert.Null(dto.Close);
            Assert.Null(dto.High252);
            Assert.Null(dto.Sma60);
            Assert.Null(dto.DrawdownPct);
            Assert.Null(dto.DistanceToTriggerPct);
            Assert.False(dto.IsStale);
        }

        [Fact]
        public async Task LessThanTwoYearsOfHistory_IsStillInitializing()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            StoreHolds(Flat(new DateOnly(2024, 10, 9), 729, 100m)); // 2024-10-09 .. 2026-10-07: 728 days apart

            Assert.True((await CreateService().GetAsync()).IsInitializing);
        }

        [Fact]
        public async Task TwoYearsOfHistory_IsEnough()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            StoreHolds(Flat(new DateOnly(2024, 10, 8), 731, 100m)); // 730 days apart

            var dto = await CreateService().GetAsync();

            Assert.False(dto.IsInitializing);
            Assert.Equal(BrakeState.OFFENSE, dto.State);
        }

        // ── the signal ──────────────────────────────────────────────────────

        [Fact]
        public async Task FlatMarket_ReportsOffense_AndZeroDrawdown()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            StoreHolds(Flat(new DateOnly(2024, 10, 1), 740, 100m));

            var dto = await CreateService().GetAsync();

            Assert.Equal(new DateOnly(2024, 10, 1).AddDays(739), dto.AsOfDate);
            Assert.Equal(BrakeState.OFFENSE, dto.State);
            Assert.Equal(BrakeState.OFFENSE, dto.PrevState);
            Assert.Null(dto.LastSwitchDate);
            Assert.Equal(100m, dto.Close);
            Assert.Equal(100m, dto.High252);
            Assert.Equal(100m, dto.Sma60);
            Assert.Equal(0m, dto.DrawdownPct);
            Assert.Equal(8m, dto.DistanceToTriggerPct);
            Assert.Null(dto.Estimate);
            Assert.False(dto.EstimateCorrected);
        }

        [Fact]
        public async Task DrawdownAndDistanceToTrigger_ArePercentages()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            var closes = Flat(new DateOnly(2024, 10, 1), 740, 100m);
            closes.Add(new IndexClose(closes[^1].Date.AddDays(1), 98.8m)); // 1.2% below the high
            StoreHolds(closes);

            var dto = await CreateService().GetAsync();

            Assert.Equal(-1.2m, dto.DrawdownPct);
            Assert.Equal(6.8m, dto.DistanceToTriggerPct);
        }

        [Fact]
        public async Task BelowTheTriggerLine_DistanceIsNegative_AndStateIsDefense()
        {
            _time.Now = Taipei(2026, 10, 8, 10);
            var closes = Flat(new DateOnly(2024, 10, 1), 740, 100m);
            closes.Add(new IndexClose(closes[^1].Date.AddDays(1), 90m));
            StoreHolds(closes);

            var dto = await CreateService().GetAsync();

            Assert.Equal(BrakeState.DEFENSE, dto.State);
            Assert.Equal(BrakeState.OFFENSE, dto.PrevState); // the switch happened on this very day
            Assert.Equal(closes[^1].Date, dto.LastSwitchDate);
            Assert.Equal(-10m, dto.DrawdownPct);
            Assert.Equal(-2m, dto.DistanceToTriggerPct);
        }

        [Fact]
        public async Task GoldenSeries_PrevStateAndLastSwitchDate_AreTheReferenceOnes()
        {
            _time.Now = Taipei(2030, 1, 1, 10);
            var golden = LoadGolden();

            // At the end of the series, and cut at every one of several points, including the
            // days either side of a switch.
            var switches = Enumerable.Range(1, golden.Count - 1).Where(i => golden[i].State != golden[i - 1].State).ToList();
            var cuts = switches.Where(i => i >= 700).Take(3).SelectMany(i => new[] { i - 1, i, i + 1 })
                .Append(golden.Count - 1).Distinct().ToList();
            Assert.True(cuts.Count >= 4);

            foreach (var cut in cuts)
            {
                StoreHolds(golden.Take(cut + 1).Select(g => new IndexClose(g.Date, g.Close)).ToList());
                var dto = await CreateService().GetAsync();

                var expectedSwitch = switches.LastOrDefault(i => i <= cut);
                Assert.Equal(golden[cut].Date, dto.AsOfDate);
                Assert.Equal(golden[cut].State, dto.State);
                Assert.Equal(golden[cut - 1].State, dto.PrevState);
                Assert.Equal(expectedSwitch == 0 ? null : golden[expectedSwitch].Date, dto.LastSwitchDate);
            }
        }

        // ── isStale ─────────────────────────────────────────────────────────

        private async Task<bool> IsStaleAsync(DateTimeOffset now, DateOnly latest)
        {
            _time.Now = now;
            return await CreateService().IsStaleAsync(latest, now.UtcDateTime.AddHours(8), CancellationToken.None);
        }

        private void MarketIs(bool? closedToday) =>
            _calendar.Setup(c => c.IsMarketClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(closedToday);

        [Fact]
        public async Task Stale_WeekdayAfter1700_TodayMissing_MarketOpen()
        {
            MarketIs(false);
            Assert.True(await IsStaleAsync(Taipei(2026, 10, 8, 17), new DateOnly(2026, 10, 7)));
        }

        [Fact]
        public async Task NotStale_WhenTheMarketWasClosedToday()
        {
            MarketIs(true);
            Assert.False(await IsStaleAsync(Taipei(2026, 10, 8, 18), new DateOnly(2026, 10, 7)));
        }

        [Fact]
        public async Task NotStale_Before1700()
        {
            MarketIs(false);
            Assert.False(await IsStaleAsync(Taipei(2026, 10, 8, 16, 59), new DateOnly(2026, 10, 7)));
        }

        [Fact]
        public async Task NotStale_OnAWeekend()
        {
            MarketIs(false);
            Assert.False(await IsStaleAsync(Taipei(2026, 10, 10, 20), new DateOnly(2026, 10, 7)));
        }

        [Fact]
        public async Task NotStale_WhenTodaysCloseIsIn()
        {
            MarketIs(false);
            Assert.False(await IsStaleAsync(Taipei(2026, 10, 8, 20), new DateOnly(2026, 10, 8)));
        }

        [Fact]
        public async Task Stale_AfterAHolidayWeekend_WhenTodayIsATradingDayWithNoData()
        {
            // Tue 2026-10-06 after 17:00; the last data is Thu 10-01, Fri 10-02 was a holiday, Mon 10-05 was not.
            MarketIs(false);
            Assert.True(await IsStaleAsync(Taipei(2026, 10, 6, 18), new DateOnly(2026, 10, 1)));
        }

        [Theory]
        [InlineData("2026-10-08T18:00", "2026-10-07", false)] // Thursday evening, Wednesday is the newest: one weekday missing
        [InlineData("2026-10-08T18:00", "2026-10-06", true)]  // Tuesday is the newest: Wednesday and Thursday missing
        [InlineData("2026-10-05T18:00", "2026-10-02", false)] // Monday evening, Friday is the newest: the weekend doesn't count
        [InlineData("2026-10-06T18:00", "2026-10-02", true)]  // Tuesday evening, Friday is the newest: Monday and Tuesday missing
        public async Task WithoutACalendar_StaleNeedsTwoOrMoreMissingWeekdays(string taipeiNow, string latest, bool expected)
        {
            MarketIs(null);
            var now = DateTime.Parse(taipeiNow);

            Assert.Equal(expected, await IsStaleAsync(
                new DateTimeOffset(now, TimeSpan.FromHours(8)).ToUniversalTime(), DateOnly.Parse(latest)));
        }

        [Fact]
        public async Task GetAsync_SetsIsStale_FromTheCalendar()
        {
            _time.Now = Taipei(2026, 10, 8, 18);
            MarketIs(false);
            StoreHolds(FlatEndingOn(new DateOnly(2026, 10, 7), 740, 100m)); // today's close (10-08) is missing

            var dto = await CreateService().GetAsync();

            Assert.True(dto.IsStale);
        }

        private sealed record GoldenDay(DateOnly Date, decimal Close, BrakeState State);

        private static List<GoldenDay> LoadGolden() =>
            File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DrawdownBrakeGolden.csv"))
                .Skip(1)
                .Where(l => l.Length > 0)
                .Select(l => l.Split(','))
                .Select(p => new GoldenDay(
                    DateOnly.Parse(p[0], CultureInfo.InvariantCulture),
                    decimal.Parse(p[1], CultureInfo.InvariantCulture),
                    Enum.Parse<BrakeState>(p[2])))
                .ToList();

        private sealed class FakeTime : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
