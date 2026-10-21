using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.DailyChallenge;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The streak used to be shown by four copies of a "broken only after a 2-day gap" rule, while
    /// submitting only kept it over a 2-day gap when a freeze was left. Someone who had already spent
    /// this week's freeze and then missed a day saw their streak as alive, and the next submit reset
    /// it to 1. Both sides now go through <see cref="StreakRules"/>.
    /// </summary>
    public class StreakRulesTests
    {
        // Wednesday; that week starts Monday 2026-10-19, the one before on 2026-10-12.
        private static readonly DateTime Today = new(2026, 10, 21);
        private static readonly DateTime ThisMonday = new(2026, 10, 19);
        private static readonly DateTime LastMonday = new(2026, 10, 12);

        private static User UserWith(
            int streak, DateTime? lastStreakDate, int freezes = 1, DateTime? lastFreezeGrant = null) => new()
        {
            Id = "streak-test-user",
            LastUpdated = Today,
            CurrentStreak = streak,
            LongestStreak = streak,
            LastStreakDate = lastStreakDate,
            StreakFreezesAvailable = freezes,
            LastFreezeGrantDate = lastFreezeGrant,
        };

        [Fact]
        public void WeekStart_IsTheMondayOfThatWeek()
        {
            Assert.Equal(ThisMonday, StreakRules.WeekStart(Today));
            Assert.Equal(ThisMonday, StreakRules.WeekStart(ThisMonday));
            Assert.Equal(ThisMonday, StreakRules.WeekStart(new DateTime(2026, 10, 25))); // Sunday
        }

        [Fact]
        public void RecordCompletion_FirstEver_StartsAtOne_AndGrantsTheWeeksFreeze()
        {
            var user = UserWith(streak: 0, lastStreakDate: null, freezes: 0, lastFreezeGrant: null);

            var change = StreakRules.RecordCompletion(user, Today);

            Assert.Equal(new StreakChange(Updated: true, FreezeUsed: false), change);
            Assert.Equal(1, user.CurrentStreak);
            Assert.Equal(1, user.LongestStreak);
            Assert.Equal(Today, user.LastStreakDate);
            Assert.Equal(StreakRules.WeeklyFreezes, user.StreakFreezesAvailable);
            Assert.Equal(Today, user.LastFreezeGrantDate);
        }

        [Fact]
        public void RecordCompletion_DayAfter_Continues()
        {
            var user = UserWith(streak: 4, lastStreakDate: Today.AddDays(-1), lastFreezeGrant: ThisMonday);

            var change = StreakRules.RecordCompletion(user, Today);

            Assert.Equal(new StreakChange(Updated: true, FreezeUsed: false), change);
            Assert.Equal(5, user.CurrentStreak);
            Assert.Equal(5, user.LongestStreak);
        }

        [Fact]
        public void RecordCompletion_OneMissedDay_SpendsAFreeze()
        {
            var user = UserWith(streak: 4, lastStreakDate: Today.AddDays(-2), freezes: 1, lastFreezeGrant: ThisMonday);

            var change = StreakRules.RecordCompletion(user, Today);

            Assert.Equal(new StreakChange(Updated: true, FreezeUsed: true), change);
            Assert.Equal(5, user.CurrentStreak);
            Assert.Equal(0, user.StreakFreezesAvailable);
        }

        [Fact]
        public void RecordCompletion_OneMissedDay_FreezeAlreadySpentThisWeek_Restarts()
        {
            var user = UserWith(streak: 4, lastStreakDate: Today.AddDays(-2), freezes: 0, lastFreezeGrant: ThisMonday);

            var change = StreakRules.RecordCompletion(user, Today);

            Assert.Equal(new StreakChange(Updated: true, FreezeUsed: false), change);
            Assert.Equal(1, user.CurrentStreak);
            Assert.Equal(4, user.LongestStreak);
        }

        [Fact]
        public void RecordCompletion_OneMissedDay_FreezeSpentLastWeek_UsesThisWeeksGrant()
        {
            // Monday after a Saturday completion: Sunday missed, last week's freeze gone, but a new
            // week has started and its freeze is granted before the gap is judged.
            var user = UserWith(streak: 4, lastStreakDate: new DateTime(2026, 10, 17), freezes: 0, lastFreezeGrant: LastMonday);

            var change = StreakRules.RecordCompletion(user, ThisMonday);

            Assert.Equal(new StreakChange(Updated: true, FreezeUsed: true), change);
            Assert.Equal(5, user.CurrentStreak);
            Assert.Equal(0, user.StreakFreezesAvailable);
            Assert.Equal(ThisMonday, user.LastFreezeGrantDate);
        }

        [Fact]
        public void RecordCompletion_TwoMissedDays_Restarts()
        {
            var user = UserWith(streak: 4, lastStreakDate: Today.AddDays(-3), freezes: 1, lastFreezeGrant: ThisMonday);

            StreakRules.RecordCompletion(user, Today);

            Assert.Equal(1, user.CurrentStreak);
            Assert.Equal(1, user.StreakFreezesAvailable);
        }

        [Fact]
        public void RecordCompletion_SameDayAgain_ChangesNothing()
        {
            var user = UserWith(streak: 4, lastStreakDate: Today, lastFreezeGrant: ThisMonday);

            var change = StreakRules.RecordCompletion(user, Today);

            Assert.Equal(new StreakChange(Updated: false, FreezeUsed: false), change);
            Assert.Equal(4, user.CurrentStreak);
        }

        [Fact]
        public void DisplayStreak_OneMissedDay_FreezeAlreadySpentThisWeek_ReadsAsBroken()
        {
            // The case the old display rule got wrong: it showed 4 here.
            var user = UserWith(streak: 4, lastStreakDate: Today.AddDays(-2), freezes: 0, lastFreezeGrant: ThisMonday);

            Assert.Equal(0, StreakRules.DisplayStreak(user, Today));
        }

        [Fact]
        public void DisplayStreak_NeverCompleted_IsTheStoredValue()
        {
            Assert.Equal(0, StreakRules.DisplayStreak(UserWith(streak: 0, lastStreakDate: null), Today));
        }

        [Fact]
        public void FreezesAvailable_CountsThisWeeksGrantBeforeItIsWritten()
        {
            Assert.Equal(0, StreakRules.FreezesAvailable(storedFreezes: 0, lastFreezeGrantDate: ThisMonday, Today));
            Assert.Equal(1, StreakRules.FreezesAvailable(storedFreezes: 0, lastFreezeGrantDate: LastMonday, Today));
            Assert.Equal(1, StreakRules.FreezesAvailable(storedFreezes: 0, lastFreezeGrantDate: null, Today));
        }

        /// <summary>
        /// For every gap and freeze state: the streak reads as alive exactly when completing today
        /// would carry it on instead of restarting it.
        /// </summary>
        [Theory]
        [InlineData(0, 1, true)]
        [InlineData(1, 1, true)]
        [InlineData(1, 0, true)]
        [InlineData(2, 1, true)]
        [InlineData(2, 0, true)]
        [InlineData(2, 1, false)]
        [InlineData(2, 0, false)]
        [InlineData(3, 1, true)]
        [InlineData(3, 0, false)]
        [InlineData(5, 1, false)]
        public void DisplayStreak_AgreesWithWhatCompletingTodayWouldDo(int gapDays, int storedFreezes, bool grantedThisWeek)
        {
            var lastGrant = grantedThisWeek ? ThisMonday : LastMonday;
            var shown = StreakRules.DisplayStreak(UserWith(7, Today.AddDays(-gapDays), storedFreezes, lastGrant), Today);

            var user = UserWith(7, Today.AddDays(-gapDays), storedFreezes, lastGrant);
            StreakRules.RecordCompletion(user, Today);
            bool carriedOn = user.CurrentStreak >= 7;

            Assert.Equal(carriedOn, shown == 7);
            Assert.True(shown is 0 or 7);
        }

        [Theory]
        [InlineData(0, "Beginner 🌱")]
        [InlineData(99, "Beginner 🌱")]
        [InlineData(100, "Learner 📖")]
        [InlineData(500, "Explorer 🧭")]
        [InlineData(2000, "Advanced 🎯")]
        [InlineData(5000, "Master 👑")]
        public void XpLevels_Thresholds(int xp, string level) => Assert.Equal(level, XpLevels.For(xp));
    }
}
