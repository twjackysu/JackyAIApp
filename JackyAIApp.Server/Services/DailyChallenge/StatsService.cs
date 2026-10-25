using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    /// <summary>
    /// The signed-in user's learning stats: this week's report, the XP leaderboard, and per-day
    /// activity for the calendar heatmap. Days are UTC days. Streaks and levels come from
    /// <see cref="StreakRules"/> and <see cref="XpLevels"/>, like every other display of them.
    /// </summary>
    public interface IStatsService
    {
        /// <summary>This week (Monday to Sunday) so far.</summary>
        Task<ServiceResult<WeeklyReportResponse>> GetWeeklyReportAsync(CancellationToken ct);

        /// <summary>The top users by XP (1–50, default 20), and the user's own place if they are not among them.</summary>
        Task<ServiceResult<LeaderboardResponse>> GetLeaderboardAsync(int limit, CancellationToken ct);

        /// <summary>Challenges and exam attempts per day, for the last <paramref name="days"/> days (1–366, default 365).</summary>
        Task<ServiceResult<ActivityResponse>> GetActivityAsync(int days, CancellationToken ct);

        /// <summary>
        /// The reader's exam attempts of the last <paramref name="days"/> days (7–90): how each exam type went and the
        /// words they answered wrong most. Only their own attempts; nothing is charged or changed.
        /// </summary>
        Task<ServiceResult<WeakSpotsResponse>> GetWeakSpotsAsync(int days, CancellationToken ct);
    }

    public class StatsService(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        TimeProvider time) : IStatsService
    {
        private DateTime Today => time.GetUtcNow().UtcDateTime.Date;

        public async Task<ServiceResult<WeeklyReportResponse>> GetWeeklyReportAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var user = await dbContext.Users.FindAsync([userId], ct);
            if (user == null)
                return UserAccountFailures.Missing;

            var today = Today;
            // Week = Monday to Sunday
            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekStart = today.AddDays(-daysSinceMonday);
            var weekEnd = weekStart.AddDays(6);
            var nextWeekStart = weekStart.AddDays(7);

            // Challenges completed this week
            var challengesThisWeek = await dbContext.DailyChallengeResults
                .Where(r => r.UserId == userId && r.ChallengeDate >= weekStart && r.ChallengeDate < nextWeekStart)
                .ToListAsync(ct);

            int challengeCount = challengesThisWeek.Count;
            int correctAnswers = challengesThisWeek.Sum(c => c.Score);
            int totalAnswers = challengesThisWeek.Sum(c => c.TotalQuestions);
            int xpFromChallenges = challengesThisWeek.Sum(c => c.XPEarned);

            // New words added this week
            int newWords = await dbContext.UserWords
                .Where(uw => uw.UserId == userId && uw.DateAdded >= weekStart && uw.DateAdded < nextWeekStart)
                .CountAsync(ct);

            // Words last reviewed this week. A word reviewed twice this week counts once.
            int wordsReviewed = await dbContext.UserWords
                .Where(uw => uw.UserId == userId && uw.LastReviewedDate >= weekStart && uw.LastReviewedDate < nextWeekStart)
                .CountAsync(ct);

            // Percentile: how many users have less XP than this user
            int totalUsers = await dbContext.Users.CountAsync(u => u.TotalXP > 0, ct);
            int usersWithLessXP = await dbContext.Users.CountAsync(u => u.TotalXP > 0 && u.TotalXP < user.TotalXP, ct);
            int percentile = totalUsers > 0 ? (int)Math.Round((double)usersWithLessXP / totalUsers * 100) : 50;

            return ServiceResult.Ok(new WeeklyReportResponse
            {
                NewWordsThisWeek = newWords,
                ChallengesCompletedThisWeek = challengeCount,
                CorrectAnswersThisWeek = correctAnswers,
                TotalAnswersThisWeek = totalAnswers,
                WordsReviewedThisWeek = wordsReviewed,
                XPEarnedThisWeek = xpFromChallenges,
                CurrentStreak = StreakRules.DisplayStreak(user, today),
                TotalXP = user.TotalXP,
                Level = XpLevels.For(user.TotalXP),
                Percentile = percentile,
                WeekStart = weekStart.ToString("yyyy-MM-dd"),
                WeekEnd = weekEnd.ToString("yyyy-MM-dd"),
            });
        }

        public async Task<ServiceResult<LeaderboardResponse>> GetLeaderboardAsync(int limit, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            var today = Today;

            // Get top users by TotalXP (only users who have earned XP)
            var topUsers = await dbContext.Users
                .Where(u => u.TotalXP > 0)
                .OrderByDescending(u => u.TotalXP)
                .ThenByDescending(u => u.CurrentStreak)
                .Take(limit)
                .Select(u => new
                {
                    u.Id, u.Name, u.Email, u.TotalXP,
                    u.CurrentStreak, u.LastStreakDate, u.StreakFreezesAvailable, u.LastFreezeGrantDate,
                })
                .ToListAsync(ct);

            int rank = 0;
            var entries = topUsers.Select(u =>
            {
                rank++;
                return new LeaderboardEntry
                {
                    Rank = rank,
                    DisplayName = AnonymizeName(u.Name, u.Email),
                    TotalXP = u.TotalXP,
                    CurrentStreak = StreakRules.DisplayStreak(
                        u.CurrentStreak, u.LastStreakDate, u.StreakFreezesAvailable, u.LastFreezeGrantDate, today),
                    Level = XpLevels.For(u.TotalXP),
                    IsCurrentUser = u.Id == userId,
                };
            }).ToList();

            // Find current user if not in top list
            LeaderboardEntry? currentUserEntry = entries.FirstOrDefault(e => e.IsCurrentUser);
            if (currentUserEntry == null)
            {
                var user = await dbContext.Users.FindAsync([userId], ct);
                if (user != null && user.TotalXP > 0)
                {
                    int userRank = await dbContext.Users.CountAsync(u => u.TotalXP > user.TotalXP, ct) + 1;
                    currentUserEntry = new LeaderboardEntry
                    {
                        Rank = userRank,
                        DisplayName = AnonymizeName(user.Name, user.Email),
                        TotalXP = user.TotalXP,
                        CurrentStreak = StreakRules.DisplayStreak(user, today),
                        Level = XpLevels.For(user.TotalXP),
                        IsCurrentUser = true,
                    };
                }
            }

            int totalUsers = await dbContext.Users.CountAsync(u => u.TotalXP > 0, ct);

            return ServiceResult.Ok(new LeaderboardResponse
            {
                Entries = entries,
                CurrentUserEntry = currentUserEntry,
                TotalUsers = totalUsers,
            });
        }

        public async Task<ServiceResult<ActivityResponse>> GetActivityAsync(int days, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (days < 1) days = 365;
            if (days > 366) days = 366;

            var today = Today;
            var start = today.AddDays(-(days - 1));

            // Daily challenges (one row per completed day)
            var challengeDays = await dbContext.DailyChallengeResults
                .Where(r => r.UserId == userId && r.ChallengeDate >= start)
                .Select(r => r.ChallengeDate)
                .ToListAsync(ct);

            // Exam attempts (multiple per day)
            var examDates = await dbContext.ExamAttempts
                .Where(e => e.UserId == userId && e.CreatedAt >= start)
                .Select(e => e.CreatedAt)
                .ToListAsync(ct);

            var counts = new Dictionary<DateTime, int>();
            void Bump(DateTime d)
            {
                var day = d.Date;
                counts[day] = counts.TryGetValue(day, out var c) ? c + 1 : 1;
            }
            foreach (var d in challengeDays) Bump(d);
            foreach (var d in examDates) Bump(d);

            var allDays = new List<ActivityDay>(days);
            for (var d = start; d <= today; d = d.AddDays(1))
            {
                allDays.Add(new ActivityDay
                {
                    Date = d.ToString("yyyy-MM-dd"),
                    Count = counts.TryGetValue(d, out var c) ? c : 0,
                });
            }

            return ServiceResult.Ok(new ActivityResponse
            {
                Days = allDays,
                ActiveDays = counts.Count,
            });
        }

        /// <summary>The fewest and most days <see cref="GetWeakSpotsAsync"/> looks back.</summary>
        public const int MinWeakSpotDays = 7, MaxWeakSpotDays = 90;

        /// <summary>The most recent attempts a weak-spot read takes, so a heavy user's window stays one small query.</summary>
        internal const int MaxWeakSpotAttempts = 2000;

        /// <summary>The words a weak-spot read names.</summary>
        public const int MaxWeakWords = 10;

        public async Task<ServiceResult<WeakSpotsResponse>> GetWeakSpotsAsync(int days, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            days = Math.Clamp(days, MinWeakSpotDays, MaxWeakSpotDays);
            var start = Today.AddDays(-(days - 1));

            var attempts = await dbContext.ExamAttempts
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.CreatedAt >= start)
                .OrderByDescending(e => e.CreatedAt)
                .Take(MaxWeakSpotAttempts)
                .Select(e => new
                {
                    e.ExamType,
                    e.IsCorrect,
                    e.WordId,
                    WordText = e.Word != null ? e.Word.WordText : null,
                    e.CreatedAt,
                })
                .ToListAsync(ct);

            var byType = attempts
                .GroupBy(a => a.ExamType)
                .Select(g => new ExamTypeResult { ExamType = g.Key, Attempts = g.Count(), Correct = g.Count(a => a.IsCorrect) })
                .OrderByDescending(t => t.Attempts)
                .ThenBy(t => t.ExamType, StringComparer.Ordinal)
                .ToList();

            // Newest first, so each word's first attempt is its latest.
            var words = attempts
                .Where(a => a.WordId != null && !string.IsNullOrWhiteSpace(a.WordText))
                .GroupBy(a => a.WordId!)
                .Select(g => new
                {
                    Word = g.First().WordText!,
                    Attempts = g.Count(),
                    Wrong = g.Count(a => !a.IsCorrect),
                    LatestCorrect = g.First().IsCorrect,
                    LastWrongAt = g.Where(a => !a.IsCorrect).Select(a => (DateTime?)a.CreatedAt).FirstOrDefault(),
                })
                .Where(w => w.Wrong > 0)
                // Still wrong before put right since; then the most misses, the worst rate, the most recent.
                .OrderBy(w => w.LatestCorrect)
                .ThenByDescending(w => w.Wrong)
                .ThenByDescending(w => (double)w.Wrong / w.Attempts)
                .ThenByDescending(w => w.LastWrongAt)
                .Take(MaxWeakWords)
                .Select(w => new WeakWord
                {
                    Word = w.Word,
                    Attempts = w.Attempts,
                    Wrong = w.Wrong,
                    LatestCorrect = w.LatestCorrect,
                    LastWrongAt = DateTime.SpecifyKind(w.LastWrongAt!.Value, DateTimeKind.Utc),
                })
                .ToList();

            return ServiceResult.Ok(new WeakSpotsResponse
            {
                Days = days,
                TotalAttempts = attempts.Count,
                ByType = byType,
                Words = words,
            });
        }

        /// <summary>
        /// Anonymize user name: show first char + asterisks.
        /// e.g. "Jacky" → "J****", null → "Anonymous"
        /// </summary>
        internal static string AnonymizeName(string? name, string? email)
        {
            string displaySource = name ?? email ?? "Anonymous";
            if (displaySource.Length <= 1) return displaySource;
            return displaySource[0] + new string('*', Math.Min(displaySource.Length - 1, 4));
        }
    }
}
