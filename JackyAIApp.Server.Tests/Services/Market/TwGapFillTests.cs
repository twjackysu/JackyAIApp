using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Tests.Services.Market
{
    /// <summary>What a TW history read still fetches, and when the quote fills the last trading day.</summary>
    public class TwGapFillTests
    {
        private static readonly DateOnly Monday = new(2026, 10, 19), Tuesday = new(2026, 10, 20), Wednesday = new(2026, 10, 21);
        private static readonly DateOnly SixMonthsBack = new(2026, 4, 21), TenDaysBack = new(2026, 10, 11);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NothingStored_FetchesTheWholeWindowTheRequestNeeds(bool deep)
        {
            var neededFrom = deep ? SixMonthsBack : TenDaysBack;

            Assert.Equal(neededFrom, TwGapFill.Plan(deep, neededFrom, storedThrough: null, Tuesday, deepFetchedBefore: false));
        }

        [Fact]
        public void StoredThroughTheLastTradingDay_FetchesNothing()
        {
            Assert.Null(TwGapFill.Plan(deep: true, SixMonthsBack, storedThrough: Tuesday, Tuesday, deepFetchedBefore: true));
            Assert.Null(TwGapFill.Plan(deep: false, TenDaysBack, storedThrough: Tuesday, Tuesday, deepFetchedBefore: false));
        }

        [Fact]
        public void AForwardGap_FetchesFromTheDayAfterTheLastStoredOne()
        {
            Assert.Equal(Tuesday, TwGapFill.Plan(deep: false, TenDaysBack, storedThrough: Monday, Tuesday, deepFetchedBefore: false));
        }

        [Fact]
        public void ADeepRequestNeverDeepFetched_FetchesTheWholeWindow_EvenWhenTheStoredBarsAreCurrent()
        {
            Assert.Equal(SixMonthsBack, TwGapFill.Plan(deep: true, SixMonthsBack, storedThrough: Tuesday, Tuesday, deepFetchedBefore: false));
        }

        [Theory]
        [InlineData(null, 7, 59, true)]   // nothing stored, pre-market
        [InlineData("2026-10-19", 7, 30, true)]   // the last trading day missing
        [InlineData("2026-10-20", 7, 30, false)]  // the last trading day stored
        [InlineData("2026-10-19", 8, 0, false)]   // the cutoff itself
        [InlineData("2026-10-19", 14, 0, false)]
        public void TheQuote_FillsAMissingLastTradingDay_OnlyBefore0800(string? storedThrough, int hour, int minute, bool expected)
        {
            var stored = storedThrough is null ? (DateOnly?)null : DateOnly.Parse(storedThrough);

            Assert.Equal(expected, TwGapFill.ShouldBackfillFromQuote(stored, Tuesday, Wednesday.ToDateTime(new TimeOnly(hour, minute))));
        }
    }
}
