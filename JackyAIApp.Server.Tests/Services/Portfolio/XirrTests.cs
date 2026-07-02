using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    public class XirrTests
    {
        private const double Tolerance = 1e-4;

        [Fact]
        public void Compute_OneYearTenPercentGain_ReturnsExactRate()
        {
            // Invest 1000, get back 1100 exactly 365 days later: 1100/1.10 = 1000.
            var d0 = new DateOnly(2025, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -1000),
                (d0.AddDays(365), 1100),
            };

            var rate = Xirr.Compute(flows);

            Assert.NotNull(rate);
            Assert.Equal(0.10, rate!.Value, Tolerance);
        }

        [Fact]
        public void Compute_OneYearTenPercentLoss_ReturnsNegativeRate()
        {
            // Invest 1000, get back 900 exactly 365 days later: 900/0.90 = 1000.
            var d0 = new DateOnly(2025, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -1000),
                (d0.AddDays(365), 900),
            };

            var rate = Xirr.Compute(flows);

            Assert.NotNull(rate);
            Assert.Equal(-0.10, rate!.Value, Tolerance);
        }

        [Fact]
        public void Compute_TwoYearCompoundGrowth_ReturnsAnnualizedRate()
        {
            // 1000 doubled at 10%/yr over 2 years: 1000 * 1.10^2 = 1210.
            var d0 = new DateOnly(2023, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -1000),
                (d0.AddDays(730), 1210),
            };

            var rate = Xirr.Compute(flows);

            Assert.NotNull(rate);
            Assert.Equal(0.10, rate!.Value, Tolerance);
        }

        [Fact]
        public void Compute_MultipleContributionsThenPayout_ReturnsPositiveRate()
        {
            // Two contributions six months apart, then a terminal value comfortably above what
            // was put in — exact rate isn't hand-derivable, but it must be a real, positive,
            // finite annualized return.
            var d0 = new DateOnly(2024, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -1000),
                (d0.AddDays(180), -1000),
                (d0.AddDays(365), 2300),
            };

            var rate = Xirr.Compute(flows);

            Assert.NotNull(rate);
            Assert.True(rate!.Value > 0);
            Assert.False(double.IsNaN(rate.Value));
            Assert.False(double.IsInfinity(rate.Value));
        }

        [Fact]
        public void Compute_FewerThanTwoFlows_ReturnsNull()
        {
            var flows = new List<(DateOnly, double)> { (new DateOnly(2025, 1, 1), -1000) };

            Assert.Null(Xirr.Compute(flows));
        }

        [Fact]
        public void Compute_NoFlows_ReturnsNull()
        {
            Assert.Null(Xirr.Compute(new List<(DateOnly, double)>()));
        }

        [Fact]
        public void Compute_AllPositiveFlows_ReturnsNull()
        {
            // No outflow (nothing invested) — the equation has no meaningful solution.
            var d0 = new DateOnly(2025, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, 500),
                (d0.AddDays(30), 500),
            };

            Assert.Null(Xirr.Compute(flows));
        }

        [Fact]
        public void Compute_AllNegativeFlows_ReturnsNull()
        {
            // No inflow (nothing ever came back) — no sign change, no solution.
            var d0 = new DateOnly(2025, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -500),
                (d0.AddDays(30), -500),
            };

            Assert.Null(Xirr.Compute(flows));
        }

        [Fact]
        public void Compute_ExtremeLoss_StillReturnsFiniteRateViaBisectionFallback()
        {
            // A near-total loss pushes the rate close to -100%, a region where Newton-Raphson's
            // derivative can vanish and the bisection fallback has to take over. The exact value
            // isn't hand-derivable, but it must land in the valid (-100%, +∞) range this solver
            // can represent, and be a large negative annualized return.
            var d0 = new DateOnly(2025, 1, 1);
            var flows = new List<(DateOnly, double)>
            {
                (d0, -100_000),
                (d0.AddDays(365), 50),
            };

            var rate = Xirr.Compute(flows);

            Assert.NotNull(rate);
            Assert.False(double.IsNaN(rate!.Value));
            Assert.False(double.IsInfinity(rate.Value));
            Assert.True(rate.Value < -0.9, $"Expected a near-total-loss rate, got {rate.Value}");
            Assert.True(rate.Value >= -1.0);
        }
    }
}
