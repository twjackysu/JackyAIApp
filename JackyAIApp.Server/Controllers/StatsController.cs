using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class StatsController : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ICurrentUser _currentUser;

        public StatsController(
            IMyResponseFactory responseFactory,
            AzureSQLDBContext dbContext,
            ICurrentUser currentUser)
        {
            _responseFactory = responseFactory;
            _dbContext = dbContext;
            _currentUser = currentUser;
        }

        /// <summary>
        /// Get the user's weekly learning report.
        /// </summary>
        [HttpGet("weekly-report")]
        public async Task<IActionResult> GetWeeklyReport()
        {
            var userId = _currentUser.RequireId();
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;
            // Week = Monday to Sunday
            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekStart = today.AddDays(-daysSinceMonday);
            var weekEnd = weekStart.AddDays(6);
            var nextWeekStart = weekStart.AddDays(7);

            // Challenges completed this week
            var challengesThisWeek = await _dbContext.DailyChallengeResults
                .Where(r => r.UserId == userId && r.ChallengeDate >= weekStart && r.ChallengeDate < nextWeekStart)
                .ToListAsync();

            int challengeCount = challengesThisWeek.Count;
            int correctAnswers = challengesThisWeek.Sum(c => c.Score);
            int totalAnswers = challengesThisWeek.Sum(c => c.TotalQuestions);
            int xpFromChallenges = challengesThisWeek.Sum(c => c.XPEarned);

            // New words added this week
            int newWords = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId && uw.DateAdded >= weekStart && uw.DateAdded < nextWeekStart)
                .CountAsync();

            // Words reviewed this week (where review count > 0 and NextReviewDate was updated this week)
            int wordsReviewed = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId && uw.ReviewCount > 0)
                .Where(uw => uw.NextReviewDate != null && uw.NextReviewDate >= weekStart)
                .CountAsync();

            // Percentile: how many users have less XP than this user
            int totalUsers = await _dbContext.Users.CountAsync(u => u.TotalXP > 0);
            int usersWithLessXP = await _dbContext.Users.CountAsync(u => u.TotalXP > 0 && u.TotalXP < user.TotalXP);
            int percentile = totalUsers > 0 ? (int)Math.Round((double)usersWithLessXP / totalUsers * 100) : 50;

            return _responseFactory.CreateOKResponse(new WeeklyReportResponse
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

        /// <summary>
        /// Get the leaderboard (top users by XP this week).
        /// </summary>
        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard([FromQuery] int limit = 20)
        {
            var userId = _currentUser.RequireId();
            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            var today = DateTime.UtcNow.Date;

            // Get top users by TotalXP (only users who have earned XP)
            var topUsers = await _dbContext.Users
                .Where(u => u.TotalXP > 0)
                .OrderByDescending(u => u.TotalXP)
                .ThenByDescending(u => u.CurrentStreak)
                .Take(limit)
                .Select(u => new
                {
                    u.Id, u.Name, u.Email, u.TotalXP,
                    u.CurrentStreak, u.LastStreakDate, u.StreakFreezesAvailable, u.LastFreezeGrantDate,
                })
                .ToListAsync();

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
                var user = await _dbContext.Users.FindAsync(userId);
                if (user != null && user.TotalXP > 0)
                {
                    int userRank = await _dbContext.Users.CountAsync(u => u.TotalXP > user.TotalXP) + 1;
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

            int totalUsers = await _dbContext.Users.CountAsync(u => u.TotalXP > 0);

            return _responseFactory.CreateOKResponse(new LeaderboardResponse
            {
                Entries = entries,
                CurrentUserEntry = currentUserEntry,
                TotalUsers = totalUsers,
            });
        }

        /// <summary>
        /// Per-day learning activity for a calendar heatmap (challenges + exam attempts).
        /// </summary>
        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity([FromQuery] int days = 365)
        {
            var userId = _currentUser.RequireId();
            if (days < 1) days = 365;
            if (days > 366) days = 366;

            var today = DateTime.UtcNow.Date;
            var start = today.AddDays(-(days - 1));

            // Daily challenges (one row per completed day)
            var challengeDays = await _dbContext.DailyChallengeResults
                .Where(r => r.UserId == userId && r.ChallengeDate >= start)
                .Select(r => r.ChallengeDate)
                .ToListAsync();

            // Exam attempts (multiple per day)
            var examDates = await _dbContext.ExamAttempts
                .Where(e => e.UserId == userId && e.CreatedAt >= start)
                .Select(e => e.CreatedAt)
                .ToListAsync();

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

            return _responseFactory.CreateOKResponse(new ActivityResponse
            {
                Days = allDays,
                ActiveDays = counts.Count,
            });
        }

        /// <summary>
        /// Anonymize user name: show first char + asterisks.
        /// e.g. "Jacky" → "J****", null → "Anonymous"
        /// </summary>
        private static string AnonymizeName(string? name, string? email)
        {
            string displaySource = name ?? email ?? "Anonymous";
            if (displaySource.Length <= 1) return displaySource;
            return displaySource[0] + new string('*', Math.Min(displaySource.Length - 1, 4));
        }
    }
}
