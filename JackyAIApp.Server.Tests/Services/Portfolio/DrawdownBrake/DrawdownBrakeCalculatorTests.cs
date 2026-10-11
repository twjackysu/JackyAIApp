using System.Globalization;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Tests.TestHelpers;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// The 回落煞車 state machine (Issue #431 §1). The synthetic cases pin each rule on its own;
    /// the golden series pins the whole thing against an independent transcription of the Boss's
    /// Python reference; the real-data cases wait on the Boss's backtest series (skipped, and
    /// reported as skipped, until Fixtures/TaiexTotalReturn.csv exists).
    /// </summary>
    public class DrawdownBrakeCalculatorTests
    {
        private static readonly DateOnly Start = new(2024, 1, 1);

        /// <summary>Consecutive calendar days from <see cref="Start"/>; the calculator only needs
        /// them ascending and distinct.</summary>
        private static List<IndexClose> Series(params decimal[] closes) =>
            closes.Select((c, i) => new IndexClose(Start.AddDays(i), c)).ToList();

        private static List<IndexClose> Repeat(decimal close, int count) =>
            Enumerable.Repeat(close, count).Select((c, i) => new IndexClose(Start.AddDays(i), c)).ToList();

        private static IndexClose Next(List<IndexClose> series, decimal close) =>
            new(series[^1].Date.AddDays(1), close);

        // ── rules ───────────────────────────────────────────────────────────

        [Fact]
        public void Compute_EmptySeries_ReturnsNothing() =>
            Assert.Empty(DrawdownBrakeCalculator.Compute([]));

        [Fact]
        public void Compute_StartsInOffense_AndStaysThereWhileNothingTriggers()
        {
            var days = DrawdownBrakeCalculator.Compute(Series(100, 101, 102, 100, 99));

            Assert.All(days, d => Assert.Equal(BrakeState.OFFENSE, d.State));
        }

        [Fact]
        public void Compute_FallBeyondEightPercent_GoesDefense()
        {
            var days = DrawdownBrakeCalculator.Compute(Series(100, 91));

            Assert.Equal(BrakeState.OFFENSE, days[0].State);
            Assert.Equal(BrakeState.DEFENSE, days[1].State);
        }

        [Fact]
        public void Compute_DrawdownExactlyEightPercent_IsNotATrigger()
        {
            var days = DrawdownBrakeCalculator.Compute(Series(100, 92));

            Assert.Equal(-0.08m, days[1].Drawdown);
            Assert.Equal(BrakeState.OFFENSE, days[1].State);
        }

        [Fact]
        public void Compute_JustBeyondEightPercent_IsATrigger()
        {
            var days = DrawdownBrakeCalculator.Compute(Series(100, 91.99m));

            Assert.Equal(BrakeState.DEFENSE, days[1].State);
        }

        [Fact]
        public void Compute_FewerThan60Closes_NeverGoesUp()
        {
            // Defense is entered on day 2 and the index climbs back above its own average — but with
            // no 60-day average yet, "up" is false and the state holds.
            var series = Series(100, 91, 95, 99, 110, 120);
            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.All(days, d => Assert.Null(d.Sma60));
            Assert.Equal(BrakeState.DEFENSE, days[^1].State);
        }

        [Fact]
        public void Compute_Sma60_IsTheMeanOfTheLast60ClosesTodayIncluded()
        {
            var closes = Enumerable.Range(1, 61).Select(i => (decimal)i).ToArray();
            var days = DrawdownBrakeCalculator.Compute(Series(closes));

            Assert.Null(days[58].Sma60);          // 59 closes
            Assert.Equal(30.5m, days[59].Sma60);  // 1..60
            Assert.Equal(31.5m, days[60].Sma60);  // 2..61
        }

        [Fact]
        public void Compute_UpAndDownOnTheSameDay_UpWins()
        {
            // Day 1 is the high (1000); the next 58 days sit at 100, so defense is entered on day 2
            // and held (no average yet). On day 60 the close of 150 is above the 60-day average
            // (115.8) — up — and 85% below the high — down. Up wins.
            var series = Series(1000);
            series.AddRange(Enumerable.Range(1, 58).Select(i => new IndexClose(Start.AddDays(i), 100m)));
            series.Add(Next(series, 150));
            Assert.Equal(60, series.Count);

            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.Equal(BrakeState.DEFENSE, days[58].State);
            Assert.True(days[59].Drawdown < -DrawdownBrakeCalculator.DdTrigger);
            Assert.True(days[59].Close > days[59].Sma60);
            Assert.Equal(BrakeState.OFFENSE, days[59].State);
        }

        [Fact]
        public void Compute_CloseEqualToTheAverage_IsNotUp()
        {
            // A 6000 high, 58 closes of 100, then 200: the 60-day average is exactly 200, so the
            // close is level with it (not above) while 97% below the high. Not up, down: defense.
            var series = Series(6000);
            series.AddRange(Enumerable.Range(1, 58).Select(i => new IndexClose(Start.AddDays(i), 100m)));
            series.Add(Next(series, 200));

            var last = DrawdownBrakeCalculator.Compute(series)[^1];

            Assert.Equal(200m, last.Sma60);
            Assert.Equal(BrakeState.DEFENSE, last.State);
        }

        [Fact]
        public void Compute_NeitherCondition_KeepsYesterdaysState_InOffense()
        {
            // 60 flat closes, then a small dip: below the average (not up) but only 1% off the high
            // (not down).
            var series = Repeat(100, 60);
            series.Add(Next(series, 99));

            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.False(days[^1].Close > days[^1].Sma60);
            Assert.Equal(BrakeState.OFFENSE, days[^1].State);
        }

        [Fact]
        public void Compute_NeitherCondition_KeepsYesterdaysState_InDefense()
        {
            // Fall 9% (defense), then recover to 7% below the high: no longer "down", still under the
            // average, so defense is held rather than released.
            var series = Repeat(100, 60);
            series.Add(Next(series, 91));
            series.Add(Next(series, 93));

            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.Equal(BrakeState.DEFENSE, days[60].State);
            Assert.False(days[61].Drawdown < -DrawdownBrakeCalculator.DdTrigger);
            Assert.False(days[61].Close > days[61].Sma60);
            Assert.Equal(BrakeState.DEFENSE, days[61].State);
        }

        [Fact]
        public void Compute_ClosingAboveTheAverage_ReleasesDefense()
        {
            var series = Repeat(100, 60);
            series.Add(Next(series, 91));   // defense
            series.Add(Next(series, 101));  // back above the ~99.8 average

            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.Equal(BrakeState.DEFENSE, days[60].State);
            Assert.Equal(BrakeState.OFFENSE, days[61].State);
        }

        // ── 252-day high ────────────────────────────────────────────────────

        [Fact]
        public void Compute_High252_UsesWhatIsThereWhenHistoryIsShorter()
        {
            var days = DrawdownBrakeCalculator.Compute(Series(100, 130, 120));

            Assert.Equal(100m, days[0].High252);
            Assert.Equal(130m, days[1].High252);
            Assert.Equal(130m, days[2].High252);
        }

        [Fact]
        public void Compute_High252_WindowIs252DaysTodayIncluded_AndDropsOlderHighs()
        {
            var series = Series(200);
            series.AddRange(Enumerable.Range(1, 253).Select(i => new IndexClose(Start.AddDays(i), 100m)));

            var days = DrawdownBrakeCalculator.Compute(series);

            Assert.Equal(200m, days[251].High252); // window = days 0..251, still holds the high
            Assert.Equal(100m, days[252].High252); // window = days 1..252, high has aged out
        }

        // ── no hidden state ─────────────────────────────────────────────────

        [Fact]
        public void Compute_SameSeriesTwice_GivesTheSameResult()
        {
            var series = LoadGolden().Select(g => new IndexClose(g.Date, g.Close)).ToList();

            Assert.Equal(DrawdownBrakeCalculator.Compute(series), DrawdownBrakeCalculator.Compute(series));
        }

        [Fact]
        public void Compute_HasNoLookahead_PrefixReplayMatches()
        {
            var series = LoadGolden().Select(g => new IndexClose(g.Date, g.Close)).ToList();
            var full = DrawdownBrakeCalculator.Compute(series);

            foreach (var n in new[] { 1, 59, 60, 61, 252, 253, 700 })
                Assert.Equal(full.Take(n), DrawdownBrakeCalculator.Compute(series.Take(n).ToList()));
        }

        // ── golden series ───────────────────────────────────────────────────

        [Fact]
        public void Compute_GoldenSeries_MatchesTheReferenceImplementationDayByDay()
        {
            var golden = LoadGolden();
            var days = DrawdownBrakeCalculator.Compute(golden.Select(g => new IndexClose(g.Date, g.Close)).ToList());

            Assert.Equal(golden.Count, days.Count);
            for (var i = 0; i < golden.Count; i++)
                Assert.True(golden[i].State == days[i].State, $"{golden[i].Date}: reference {golden[i].State}, got {days[i].State}");

            // The series is only worth anything as a check if it exercises both states and many switches.
            Assert.True(golden.Zip(golden.Skip(1)).Count(p => p.First.State != p.Second.State) >= 20);
            Assert.Contains(golden, g => g.State == BrakeState.DEFENSE);
            Assert.Contains(golden, g => g.State == BrakeState.OFFENSE);
        }

        // ── Boss's real series (waiting on Fixtures/TaiexTotalReturn.csv) ───

        [RequiresFixtureFact("TaiexTotalReturn.csv")]
        public void Compute_BossBacktestSeries_EndsOffenseNearTheHigh()
        {
            var closes = LoadCloses("TaiexTotalReturn.csv");

            var last = DrawdownBrakeCalculator.Compute(closes)[^1];

            Assert.Equal(new DateOnly(2026, 10, 8), last.Date);
            Assert.Equal(BrakeState.OFFENSE, last.State);
            Assert.InRange(last.Drawdown * 100, -1.5m, -0.5m); // about −1%, ±0.5 percentage points
        }

        [RequiresFixtureFact("TaiexTotalReturn.csv")]
        public void Compute_BossBacktestSeries_LastThreeYearsAloneReachTheSameState()
        {
            var closes = LoadCloses("TaiexTotalReturn.csv");
            var full = DrawdownBrakeCalculator.Compute(closes)[^1];

            var cutoff = closes[^1].Date.AddYears(-3);
            var lastThreeYears = closes.Where(c => c.Date >= cutoff).ToList();
            var short3 = DrawdownBrakeCalculator.Compute(lastThreeYears)[^1];

            Assert.Equal(full.State, short3.State);
            Assert.Equal(full.High252, short3.High252);
            Assert.Equal(full.Sma60, short3.Sma60);
        }

        // ── helpers ─────────────────────────────────────────────────────────

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

        /// <summary>The Boss's <c>date,close</c> file; a header row, if present, is skipped.</summary>
        private static List<IndexClose> LoadCloses(string fileName) =>
            File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName))
                .Select(l => l.Split(','))
                .Where(p => p.Length >= 2 && DateOnly.TryParse(p[0], CultureInfo.InvariantCulture, out _))
                .Select(p => new IndexClose(DateOnly.Parse(p[0], CultureInfo.InvariantCulture), decimal.Parse(p[1], CultureInfo.InvariantCulture)))
                .OrderBy(c => c.Date)
                .ToList();
    }
}
