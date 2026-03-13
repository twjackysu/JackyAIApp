using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
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
        private readonly IUserService _userService;

        public StatsController(
            IMyResponseFactory responseFactory,
            AzureSQLDBContext dbContext,
            IUserService userService)
        {
            _responseFactory = responseFactory;
            _dbContext = dbContext;
            _userService = userService;
        }

        /// <summary>
        /// Get the user's weekly learning report.
        /// </summary>
        [HttpGet("weekly-report")]
        public async Task<IActionResult> GetWeeklyReport()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

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

            // Validate streak is current
            int displayStreak = user.CurrentStreak;
            if (user.LastStreakDate.HasValue && (today - user.LastStreakDate.Value).Days > 1)
                displayStreak = 0;

            return _responseFactory.CreateOKResponse(new WeeklyReportResponse
            {
                NewWordsThisWeek = newWords,
                ChallengesCompletedThisWeek = challengeCount,
                CorrectAnswersThisWeek = correctAnswers,
                TotalAnswersThisWeek = totalAnswers,
                WordsReviewedThisWeek = wordsReviewed,
                XPEarnedThisWeek = xpFromChallenges,
                CurrentStreak = displayStreak,
                TotalXP = user.TotalXP,
                Level = GetLevel(user.TotalXP),
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
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            var today = DateTime.UtcNow.Date;

            // Get top users by TotalXP (only users who have earned XP)
            var topUsers = await _dbContext.Users
                .Where(u => u.TotalXP > 0)
                .OrderByDescending(u => u.TotalXP)
                .ThenByDescending(u => u.CurrentStreak)
                .Take(limit)
                .Select(u => new { u.Id, u.Name, u.Email, u.TotalXP, u.CurrentStreak, u.LastStreakDate })
                .ToListAsync();

            int rank = 0;
            var entries = topUsers.Select(u =>
            {
                rank++;
                // Validate streak
                int streak = u.CurrentStreak;
                if (u.LastStreakDate.HasValue && (today - u.LastStreakDate.Value).Days > 1)
                    streak = 0;

                return new LeaderboardEntry
                {
                    Rank = rank,
                    DisplayName = AnonymizeName(u.Name, u.Email),
                    TotalXP = u.TotalXP,
                    CurrentStreak = streak,
                    Level = GetLevel(u.TotalXP),
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
                    int streak = user.CurrentStreak;
                    if (user.LastStreakDate.HasValue && (today - user.LastStreakDate.Value).Days > 1)
                        streak = 0;

                    currentUserEntry = new LeaderboardEntry
                    {
                        Rank = userRank,
                        DisplayName = AnonymizeName(user.Name, user.Email),
                        TotalXP = user.TotalXP,
                        CurrentStreak = streak,
                        Level = GetLevel(user.TotalXP),
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

        private static string GetLevel(int xp) => xp switch
        {
            >= 5000 => "Master 👑",
            >= 2000 => "Advanced 🎯",
            >= 500 => "Explorer 🧭",
            >= 100 => "Learner 📖",
            _ => "Beginner 🌱"
        };

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
