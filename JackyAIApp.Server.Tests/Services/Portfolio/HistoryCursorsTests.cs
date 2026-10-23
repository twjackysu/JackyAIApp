using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.Services.Portfolio
{
    /// <summary>The forward cursors the history loops walk: a price on or before each day, and the snapshot in effect.</summary>
    public class HistoryCursorsTests
    {
        private static DailyPrice Bar(int year, int month, int day, decimal close, int hour = 0) =>
            new() { Date = new DateTime(year, month, day, hour, 0, 0), Close = close };

        [Fact]
        public void StockCursor_GivesTheLastCloseOnOrBeforeEachDay_NullBeforeTheFirst()
        {
            // Out of order on purpose: the cursor sorts its own copy.
            var cursor = ForwardPriceCursor.ForStock([Bar(2026, 8, 5, 105m), Bar(2026, 8, 3, 103m)]);

            Assert.Null(cursor.CloseOnOrBefore(new DateOnly(2026, 8, 2)));
            Assert.Equal(103m, cursor.CloseOnOrBefore(new DateOnly(2026, 8, 3)));
            Assert.Equal(103m, cursor.CloseOnOrBefore(new DateOnly(2026, 8, 4)));
            Assert.Equal(105m, cursor.CloseOnOrBefore(new DateOnly(2026, 8, 9)));
        }

        [Fact]
        public void StockCursor_TwoClosesOnOneDay_TheLaterOneWins() =>
            Assert.Equal(222m, ForwardPriceCursor.ForStock([Bar(2026, 8, 5, 222m, hour: 13), Bar(2026, 8, 5, 105m, hour: 9)])
                .CloseOnOrBefore(new DateOnly(2026, 8, 5)));

        [Fact]
        public void FundCursor_ComparesIsoDatesAsDates() =>
            Assert.Equal(10.1m, ForwardPriceCursor.ForFund(
                    [new PricePoint { Date = "2026-08-03", Close = 10.1m }, new PricePoint { Date = "2026-08-10", Close = 10.2m }])
                .CloseOnOrBefore("2026-08-09"));

        [Fact]
        public void AnEmptyHistory_HasNoClose() =>
            Assert.Null(ForwardPriceCursor.ForStock([]).CloseOnOrBefore(new DateOnly(2026, 8, 5)));

        [Fact]
        public void SnapshotCursor_GivesTheLatestSnapshotOnOrBeforeEachDay_NullBeforeTheFirst()
        {
            HoldingSnapshot Snapshot(int day) => new() { UserId = "u", EffectiveDate = new DateOnly(2026, 8, day) };
            var first = Snapshot(3);
            var second = Snapshot(10);
            var cursor = new SnapshotCursor([first, second]);

            Assert.Null(cursor.ActiveOn(new DateOnly(2026, 8, 2)));
            Assert.Same(first, cursor.ActiveOn(new DateOnly(2026, 8, 3)));
            Assert.Same(first, cursor.ActiveOn(new DateOnly(2026, 8, 9)));
            Assert.Same(second, cursor.ActiveOn(new DateOnly(2026, 8, 10)));
        }
    }
}
