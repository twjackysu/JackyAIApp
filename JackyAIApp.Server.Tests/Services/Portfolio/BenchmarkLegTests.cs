using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>One benchmark bought with the user's contributions.</summary>
    public class BenchmarkLegTests
    {
        private static readonly DateOnly Mon = new(2026, 8, 3), Tue = Mon.AddDays(1), Wed = Mon.AddDays(2);

        private static BenchmarkLeg Leg(Dictionary<DateOnly, decimal>? dividends = null) => new("0050",
            ForwardPriceCursor.ForStock([Price(Mon, 100m), Price(Tue, 110m), Price(Wed, 120m)]), dividends ?? []);

        private static DailyPrice Price(DateOnly day, decimal close) => new() { Date = day.ToDateTime(TimeOnly.MinValue), Close = close };

        [Fact]
        public void AContribution_BuysUnitsAtThatDaysPrice()
        {
            var leg = Leg();
            leg.Invest(1_000m, Mon);   // 10 units
            leg.Record(Mon, 1_000m);
            leg.Record(Tue, 0);

            Assert.Equal(new[] { 1_000m, 1_100m }, leg.Values);
            Assert.Equal(10.00m, leg.Return.Pct);
        }

        [Fact]
        public void AWithdrawal_SellsUnits_NeverBelowNone()
        {
            var leg = Leg();
            leg.Invest(1_000m, Mon);
            leg.Invest(-5_000m, Tue);
            leg.Record(Tue, -5_000m);

            Assert.Equal(0m, Assert.Single(leg.Values));
        }

        [Fact]
        public void Seed_BuysWhatWasInvestedBeforeTheWindow_AtItsStartPrice()
        {
            var leg = Leg();
            leg.Seed(2_200m, Tue);   // 20 units at 110
            leg.Record(Wed, 0);

            Assert.Equal(2_400m, Assert.Single(leg.Values));
        }

        [Fact]
        public void ADividend_IsIncomeOnTheUnitsHeldGoingIntoItsExDate()
        {
            var leg = Leg(new Dictionary<DateOnly, decimal> { [Tue] = 1m });
            leg.Invest(1_000m, Mon);   // 10 units
            leg.Record(Mon, 1_000m);
            leg.AccrueDividends(Tue);  // 10 × 1 = 10 income
            leg.Record(Tue, 0);

            // (1,100 + 10) ÷ 1,000
            Assert.Equal(11.00m, leg.Return.Pct);
        }
    }
}
