using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Tests.Services.Portfolio.DrawdownBrake
{
    public class DrawdownBrakeEstimatorTests
    {
        private static readonly DateOnly Today = new(2026, 10, 8);
        private static readonly DateTimeOffset QuotedAt = new(2026, 10, 8, 13, 25, 10, TimeSpan.FromHours(8));

        private static MisIndexQuote Quote(decimal last, decimal previous) => new(Today, QuotedAt, last, previous);

        /// <summary>Consecutive days ending the day before <see cref="Today"/>, each with the given close.</summary>
        private static List<IndexClose> Series(params (int Days, decimal Close)[] runs)
        {
            var total = runs.Sum(r => r.Days);
            var date = Today.AddDays(-total);
            var closes = new List<IndexClose>();
            foreach (var (days, close) in runs)
                for (var i = 0; i < days; i++)
                    closes.Add(new IndexClose(date = date.AddDays(1), close));
            return closes;
        }

        [Fact]
        public void TheEstimate_IsYesterdaysOfficialClose_MovedByThePriceIndexsMove()
        {
            // 23,100 / 23,000 = +0.4348%; the official close moves by the same factor.
            var official = Series((300, 20000m));

            var estimate = DrawdownBrakeEstimator.Estimate(official, Quote(23100m, 23000m));

            Assert.Equal(Math.Round(20000m * 23100m / 23000m, 4), estimate.EstimatedClose);
            Assert.Equal(20086.9565m, estimate.EstimatedClose);
        }

        [Fact]
        public void AFlatDay_EstimatesTheOfficialCloseUnchanged()
        {
            var estimate = DrawdownBrakeEstimator.Estimate(Series((300, 100m)), Quote(23000m, 23000m));

            Assert.Equal(100m, estimate.EstimatedClose);
            Assert.Equal(0m, estimate.DrawdownPct);
            Assert.Equal(BrakeState.OFFENSE, estimate.State);
            Assert.Equal(BrakeState.OFFENSE, estimate.PrevState);
        }

        [Fact]
        public void TheEstimatedClose_IsRoundedToFourDecimals_LikeAStoredClose()
        {
            var estimate = DrawdownBrakeEstimator.Estimate(Series((300, 100m)), Quote(23001m, 23000m));

            Assert.Equal(100.0043m, estimate.EstimatedClose); // 100 × 23001/23000 = 100.004347…
        }

        [Fact]
        public void TheAsOfTime_IsWhenMisComputedTheIndex()
        {
            var estimate = DrawdownBrakeEstimator.Estimate(Series((300, 100m)), Quote(23000m, 23000m));

            Assert.Equal(QuotedAt, estimate.AsOfTime);
        }

        [Fact]
        public void AFallThroughTheTrigger_EstimatesDefense_FromAnOffensePrevState()
        {
            // 100 for a year, then 95 (−5%: not a trigger). Down another 4% today: 91.2, −8.8%.
            var official = Series((250, 100m), (5, 95m));

            var estimate = DrawdownBrakeEstimator.Estimate(official, Quote(22080m, 23000m));

            Assert.Equal(91.2m, estimate.EstimatedClose);
            Assert.Equal(-8.8m, estimate.DrawdownPct);
            Assert.Equal(BrakeState.DEFENSE, estimate.State);
            Assert.Equal(BrakeState.OFFENSE, estimate.PrevState);
        }

        [Fact]
        public void AnExactlyEightPercentFall_IsNotATrigger_AsInTheOfficialRule()
        {
            var estimate = DrawdownBrakeEstimator.Estimate(Series((300, 100m)), Quote(21160m, 23000m)); // ×0.92

            Assert.Equal(92m, estimate.EstimatedClose);
            Assert.Equal(-8m, estimate.DrawdownPct);
            Assert.Equal(BrakeState.OFFENSE, estimate.State);
        }

        [Fact]
        public void ARiseAboveTheSixtyDayAverage_EstimatesOffense_FromADefensePrevState()
        {
            // 200 days at 100, then 70 at 80: DEFENSE since the first −20%. Today +10% → 88, above the 60-day mean.
            var official = Series((200, 100m), (70, 80m));

            var estimate = DrawdownBrakeEstimator.Estimate(official, Quote(25300m, 23000m));

            Assert.Equal(88m, estimate.EstimatedClose);
            Assert.Equal(BrakeState.OFFENSE, estimate.State);
            Assert.Equal(BrakeState.DEFENSE, estimate.PrevState);
        }

        [Fact]
        public void ARiseThatStaysBelowTheSixtyDayAverage_StaysDefense()
        {
            // Still sliding: the last 60 closes average about 74.8, well above today's 70.3.
            var official = Series((200, 100m), (40, 80m), (30, 70m));

            var estimate = DrawdownBrakeEstimator.Estimate(official, Quote(23100m, 23000m)); // +0.43% → 70.3

            Assert.Equal(BrakeState.DEFENSE, estimate.State);
            Assert.Equal(BrakeState.DEFENSE, estimate.PrevState);
        }

        [Fact]
        public void TheEstimate_IsTheOfficialCalculatorOverTheSeriesWithTodaysPointAdded()
        {
            var official = Series((120, 100m), (60, 110m), (40, 90m), (30, 95m));
            var quote = Quote(22700m, 23000m);

            var estimate = DrawdownBrakeEstimator.Estimate(official, quote);

            var replay = DrawdownBrakeCalculator.Compute([.. official, new IndexClose(Today, estimate.EstimatedClose)]);
            Assert.Equal(replay[^1].State, estimate.State);
            Assert.Equal(replay[^2].State, estimate.PrevState);
            Assert.Equal(Math.Round(replay[^1].Drawdown * 100, 4), estimate.DrawdownPct);
        }

        [Fact]
        public void TheOfficialSeries_IsNotModified()
        {
            var official = Series((300, 100m));
            var before = official.ToList();

            DrawdownBrakeEstimator.Estimate(official, Quote(22080m, 23000m));

            Assert.Equal(before, official);
        }

        [Fact]
        public void WithNoOfficialClose_ThereIsNothingToScale()
        {
            Assert.Throws<ArgumentException>(() => DrawdownBrakeEstimator.Estimate([], Quote(23000m, 23000m)));
        }
    }
}
