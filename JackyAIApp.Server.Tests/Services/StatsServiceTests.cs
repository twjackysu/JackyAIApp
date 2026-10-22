using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The learning stats, now behind <see cref="StatsService"/>: what the weekly report counts and
    /// where its week starts, how the leaderboard ranks and anonymizes, and the activity heatmap.
    /// </summary>
    public class StatsServiceTests : IDisposable
    {
        private readonly AzureSQLDBContext _db = new(new DbContextOptionsBuilder<AzureSQLDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        /// <summary>Wednesday 2026-10-21, mid-afternoon UTC: the week is Mon 10-19 to Sun 10-25.</summary>
        private static readonly DateTime Today = new(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _db.Dispose();

        private sealed class Clock(DateTime now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
        }

        private StatsService As(string userId, DateTime? now = null)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(userId);
            return new StatsService(_db, user.Object, new Clock(now ?? Today.AddHours(15)));
        }

        private User AddUser(string id, int xp = 0, string? name = null, string? email = null, int streak = 0, DateTime? lastStreakDate = null)
        {
            var user = new User
            {
                Id = id, Name = name, Email = email, LastUpdated = Today, TotalXP = xp,
                CurrentStreak = streak, LastStreakDate = lastStreakDate,
            };
            _db.Users.Add(user);
            return user;
        }

        private void AddChallenge(string userId, DateTime day, int score = 5, int total = 5, int xp = 50) =>
            _db.DailyChallengeResults.Add(new DailyChallengeResult
            {
                UserId = userId, ChallengeDate = day, Score = score, TotalQuestions = total, XPEarned = xp, CompletedAt = day,
            });

        private void AddWord(string userId, DateTime added, int reviewCount = 0, DateTime? nextReview = null) =>
            _db.UserWords.Add(new UserWord
            {
                UserId = userId, WordId = Guid.NewGuid().ToString(), DateAdded = added,
                ReviewCount = reviewCount, NextReviewDate = nextReview,
            });

        private void AddExam(string userId, DateTime at) =>
            _db.ExamAttempts.Add(new ExamAttempt { UserId = userId, ExamType = "cloze", IsCorrect = true, CreatedAt = at });

        [Fact]
        public async Task WeeklyReport_CountsOnlyYourOwnThisWeek()
        {
            AddUser("alice", xp: 300, streak: 4, lastStreakDate: Today.AddDays(-1));
            AddChallenge("alice", Today.AddDays(-2), score: 4, xp: 40);  // Monday
            AddChallenge("alice", Today, score: 5, xp: 50);
            AddChallenge("alice", Today.AddDays(-3));                    // last Sunday
            AddChallenge("bob", Today.AddDays(-1));
            AddWord("alice", Today.AddDays(-2).AddHours(9));
            AddWord("alice", Today.AddHours(1));
            AddWord("alice", Today.AddDays(-3).AddHours(23));            // last Sunday
            AddWord("bob", Today);
            AddWord("alice", Today.AddDays(-30), reviewCount: 1, nextReview: Today.AddDays(3));
            AddWord("alice", Today.AddDays(-30), reviewCount: 0, nextReview: Today.AddDays(3));
            AddWord("alice", Today.AddDays(-30), reviewCount: 2, nextReview: Today.AddDays(-6));
            await _db.SaveChangesAsync();

            var report = (await As("alice").GetWeeklyReportAsync(default)).Data!;

            Assert.Equal(("2026-10-19", "2026-10-25"), (report.WeekStart, report.WeekEnd));
            Assert.Equal(2, report.ChallengesCompletedThisWeek);
            Assert.Equal((9, 10, 90), (report.CorrectAnswersThisWeek, report.TotalAnswersThisWeek, report.XPEarnedThisWeek));
            Assert.Equal(2, report.NewWordsThisWeek);
            Assert.Equal(1, report.WordsReviewedThisWeek);
            Assert.Equal((4, 300, "Learner 📖"), (report.CurrentStreak, report.TotalXP, report.Level));
        }

        [Theory]
        [InlineData("2026-10-19", "2026-10-19")] // Monday starts its own week
        [InlineData("2026-10-25", "2026-10-19")] // Sunday ends it
        [InlineData("2026-10-26", "2026-10-26")]
        public async Task TheWeek_RunsMondayToSunday(string today, string weekStart)
        {
            AddUser("alice");
            await _db.SaveChangesAsync();

            var report = (await As("alice", DateTime.Parse(today).AddHours(23)).GetWeeklyReportAsync(default)).Data!;

            Assert.Equal(weekStart, report.WeekStart);
        }

        [Fact]
        public async Task Percentile_IsTheShareOfUsersWithXpBelowYou()
        {
            AddUser("alice", xp: 300);
            AddUser("bob", xp: 100);
            AddUser("carol", xp: 500);
            AddUser("dave", xp: 0); // has not played: not counted
            await _db.SaveChangesAsync();

            Assert.Equal(33, (await As("alice").GetWeeklyReportAsync(default)).Data!.Percentile);
            Assert.Equal(0, (await As("bob").GetWeeklyReportAsync(default)).Data!.Percentile);
        }

        [Fact]
        public async Task Percentile_IsHalfway_WhenNobodyHasXp()
        {
            AddUser("alice");
            await _db.SaveChangesAsync();

            Assert.Equal(50, (await As("alice").GetWeeklyReportAsync(default)).Data!.Percentile);
        }

        [Fact]
        public async Task WeeklyReport_WithoutTheUsersRow_AsksToSignInAgain()
        {
            var result = await As("ghost").GetWeeklyReportAsync(default);

            Assert.Equal(ErrorCodes.Unauthorized, result.Error);
            Assert.Equal(UserAccountFailures.Missing.Message, result.ErrorMessage);
        }

        [Fact]
        public async Task Leaderboard_RanksByXpThenStreak_Anonymized_WithYourPlaceBelowTheTop()
        {
            AddUser("u1", xp: 500, name: "Jacky", streak: 1, lastStreakDate: Today);
            AddUser("u2", xp: 500, name: "Amy", streak: 3, lastStreakDate: Today);
            AddUser("u3", xp: 300, email: "bob@example.com");
            AddUser("u4", xp: 200, name: "Z");
            AddUser("alice", xp: 100, name: "Alice", streak: 7, lastStreakDate: Today.AddDays(-5));
            AddUser("idle", xp: 0, name: "Idle");
            await _db.SaveChangesAsync();

            var board = (await As("alice").GetLeaderboardAsync(2, default)).Data!;

            Assert.Equal(["A**", "J****"], board.Entries.Select(e => e.DisplayName));
            Assert.Equal([1, 2], board.Entries.Select(e => e.Rank));
            Assert.All(board.Entries, e => Assert.False(e.IsCurrentUser));
            var me = board.CurrentUserEntry!;
            Assert.Equal((5, "A****", true), (me.Rank, me.DisplayName, me.IsCurrentUser));
            Assert.Equal(0, me.CurrentStreak); // its last day was five days ago
            Assert.Equal(5, board.TotalUsers);

            var all = (await As("u3").GetLeaderboardAsync(0, default)).Data!; // 0 → the default 20
            Assert.Equal(["A**", "J****", "b****", "Z", "A****"], all.Entries.Select(e => e.DisplayName));
            Assert.True(all.Entries[2].IsCurrentUser);
            Assert.Same(all.Entries[2], all.CurrentUserEntry);
        }

        [Fact]
        public async Task Leaderboard_HasNoPlaceForYou_UntilYouHaveXp()
        {
            AddUser("u1", xp: 500, name: "Jacky");
            AddUser("alice", xp: 0, name: "Alice");
            await _db.SaveChangesAsync();

            var board = (await As("alice").GetLeaderboardAsync(20, default)).Data!;

            Assert.Null(board.CurrentUserEntry);
            Assert.Equal(1, board.TotalUsers);
        }

        [Fact]
        public async Task Activity_CountsYourChallengesAndExamsPerDay_OverTheLastDays()
        {
            AddChallenge("alice", Today.AddDays(-1));
            AddExam("alice", Today.AddDays(-1).AddHours(8));
            AddExam("alice", Today.AddDays(-1).AddHours(20));
            AddExam("alice", Today.AddHours(2));
            AddExam("alice", Today.AddDays(-7)); // one day before the window
            AddExam("bob", Today);
            await _db.SaveChangesAsync();

            var activity = (await As("alice").GetActivityAsync(7, default)).Data!;

            Assert.Equal(7, activity.Days.Count);
            Assert.Equal(("2026-10-15", "2026-10-21"), (activity.Days[0].Date, activity.Days[^1].Date));
            Assert.Equal([0, 0, 0, 0, 0, 3, 1], activity.Days.Select(d => d.Count));
            Assert.Equal(2, activity.ActiveDays);
        }

        [Theory]
        [InlineData(0, 365)]
        [InlineData(1000, 366)]
        [InlineData(1, 1)]
        public async Task Activity_Window_IsOneTo366Days(int asked, int expected)
        {
            Assert.Equal(expected, (await As("alice").GetActivityAsync(asked, default)).Data!.Days.Count);
        }
    }
}
