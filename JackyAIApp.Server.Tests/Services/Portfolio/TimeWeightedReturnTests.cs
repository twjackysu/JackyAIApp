using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>The time-weighted return the benchmark comparison reports for every curve.</summary>
    public class TimeWeightedReturnTests
    {
        [Fact]
        public void ChainsEachDaysGrowth()
        {
            var twr = new TimeWeightedReturn();
            twr.Record(100m, contribution: 100m, income: 0);
            twr.Record(110m, contribution: 0, income: 0);
            twr.Record(121m, contribution: 0, income: 0);

            Assert.Equal(21.00m, twr.Pct);
        }

        [Fact]
        public void ADeposit_IsNotAGain()
        {
            var twr = new TimeWeightedReturn();
            twr.Record(100m, contribution: 100m, income: 0);
            twr.Record(200m, contribution: 100m, income: 0);

            Assert.Equal(0.00m, twr.Pct);
        }

        [Fact]
        public void Income_CountsInItsDay_AndOnlyOnce()
        {
            var twr = new TimeWeightedReturn();
            twr.Record(100m, contribution: 100m, income: 0);
            twr.Record(100m, contribution: 0, income: 5m);   // +5%
            twr.Record(100m, contribution: 0, income: 0);    // the cash left the base: no more growth

            Assert.Equal(5.00m, twr.Pct);
        }

        [Fact]
        public void ADayWithANegativeBase_IsSkipped_NotFlippedInSign()
        {
            var twr = new TimeWeightedReturn();
            twr.Record(100m, contribution: 100m, income: 0);
            twr.Record(50m, contribution: -150m, income: 0);  // base 100 − 150 < 0: skipped
            twr.Record(55m, contribution: 0, income: 0);      // 55 ÷ 50

            Assert.Equal(10.00m, twr.Pct);
        }

        [Fact]
        public void ADayWithNoBase_IsSkipped()
        {
            var twr = new TimeWeightedReturn();
            twr.Record(100m, contribution: 100m, income: 0);
            twr.Record(0m, contribution: -100m, income: 0);  // all withdrawn: base 0, skipped
            twr.Record(50m, contribution: 50m, income: 0);   // base 0 + 50 = 50, value 50: no growth

            Assert.Equal(0.00m, twr.Pct);
        }
    }
}
