using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using Microsoft.EntityFrameworkCore;
using static JackyAIApp.Server.Services.DailyChallenge.DailyChallengeConstants;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    public class DailyChallengeService(
        AzureSQLDBContext dbContext,
        IUserService userService,
        ICreditService creditService,
        IDailyChallengeQuestionGenerator questionGenerator) : IDailyChallengeService
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly ICreditService _creditService = creditService;
        private readonly IDailyChallengeQuestionGenerator _questionGenerator = questionGenerator;

        public async Task<ServiceResult> GetChallengeAsync()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;

            var existingResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            var words = await LoadEligibleWordsAsync();
            if (words.Count < MinWordsForChallenge)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest,
                    $"Not enough words in the database to generate a challenge. Need at least {MinWordsForChallenge}.");
            }

            var questions = _questionGenerator.Generate(words, today);

            return ServiceResult.Ok(new DailyChallengeResponse
            {
                Questions = questions,
                ChallengeDate = today.ToString("yyyy-MM-dd"),
                AlreadyCompleted = existingResult != null,
                PreviousScore = existingResult?.Score
            });
        }

        public async Task<ServiceResult> SubmitChallengeAsync(DailyChallengeSubmitRequest request)
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;

            var existingResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            if (existingResult != null)
            {
                return ServiceResult.Ok(new DailyChallengeSubmitResponse
                {
                    Score = existingResult.Score,
                    TotalQuestions = existingResult.TotalQuestions,
                    XPEarned = 0,
                    StreakUpdated = false,
                    NewStreak = (await _dbContext.Users.FindAsync(userId))?.CurrentStreak ?? 0,
                    AlreadyCompleted = true
                });
            }

            var words = await LoadEligibleWordsAsync();
            var questions = _questionGenerator.Generate(words, today);

            int score = 0;
            if (request.Answers != null)
            {
                foreach (var answer in request.Answers)
                {
                    var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                    if (question != null && answer.SelectedIndex == question.CorrectIndex)
                    {
                        score++;
                    }
                }
            }

            int xpEarned = score == QuestionsPerChallenge ? 50 : score * 8;

            _dbContext.DailyChallengeResults.Add(new DailyChallengeResult
            {
                UserId = userId,
                ChallengeDate = today,
                Score = score,
                TotalQuestions = QuestionsPerChallenge,
                XPEarned = xpEarned,
                CompletedAt = DateTime.UtcNow
            });

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            bool streakUpdated = false;
            var yesterday = today.AddDays(-1);

            if (user.LastStreakDate == null || user.LastStreakDate < yesterday)
            {
                user.CurrentStreak = 1;
                streakUpdated = true;
            }
            else if (user.LastStreakDate == yesterday)
            {
                user.CurrentStreak++;
                streakUpdated = true;
            }

            if (streakUpdated)
            {
                user.LastStreakDate = today;
                if (user.CurrentStreak > user.LongestStreak)
                {
                    user.LongestStreak = user.CurrentStreak;
                }
            }

            user.TotalXP += xpEarned;

            await _dbContext.SaveChangesAsync();

            // Award credit bonuses (after SaveChanges to avoid transaction conflicts)
            ulong creditsAwarded = 0;

            await _creditService.AddCreditsAsync(userId, CreditCosts.DailyChallengeBonus,
                CreditTransactionType.Bonus, "daily_challenge",
                $"Daily challenge completed ({score}/{QuestionsPerChallenge})");
            creditsAwarded += CreditCosts.DailyChallengeBonus;

            if (user.CurrentStreak == 7 || user.CurrentStreak == 30 || user.CurrentStreak == 100)
            {
                await _creditService.AddCreditsAsync(userId, CreditCosts.StreakMilestone7Day,
                    CreditTransactionType.Bonus, "streak_milestone",
                    $"🔥 {user.CurrentStreak}-day streak milestone!");
                creditsAwarded += CreditCosts.StreakMilestone7Day;
            }

            return ServiceResult.Ok(new DailyChallengeSubmitResponse
            {
                Score = score,
                TotalQuestions = QuestionsPerChallenge,
                XPEarned = xpEarned,
                StreakUpdated = streakUpdated,
                NewStreak = user.CurrentStreak,
                AlreadyCompleted = false,
                CreditsAwarded = creditsAwarded
            });
        }

        public async Task<ServiceResult> GetStatsAsync()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;
            var todayResult = await _dbContext.DailyChallengeResults
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ChallengeDate == today);

            int displayStreak = user.CurrentStreak;
            if (user.LastStreakDate.HasValue)
            {
                var daysSinceLastStreak = (today - user.LastStreakDate.Value).Days;
                if (daysSinceLastStreak > 1)
                {
                    displayStreak = 0;
                }
            }

            return ServiceResult.Ok(new DailyChallengeStatsResponse
            {
                CurrentStreak = displayStreak,
                LongestStreak = user.LongestStreak,
                TotalXP = user.TotalXP,
                Level = GetLevel(user.TotalXP),
                TodayCompleted = todayResult != null,
                TodayScore = todayResult?.Score
            });
        }

        public async Task<ServiceResult> ClaimDailyBonusAsync()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return ServiceResult.Fail(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;

            var alreadyClaimed = await _dbContext.CreditTransactions
                .AnyAsync(t => t.UserId == userId
                    && t.Reason == "daily_login_bonus"
                    && t.CreatedAt >= today && t.CreatedAt < today.AddDays(1));

            if (alreadyClaimed)
            {
                return ServiceResult.Ok(new { claimed = false, message = "Already claimed today" });
            }

            await _creditService.AddCreditsAsync(userId, CreditCosts.DailyLoginBonus,
                CreditTransactionType.Bonus, "daily_login_bonus",
                "Daily login bonus");

            return ServiceResult.Ok(new { claimed = true, credits = CreditCosts.DailyLoginBonus });
        }

        private Task<List<Data.Models.SQL.Word>> LoadEligibleWordsAsync() =>
            _dbContext.Words
                .Where(w => w.DataInvalid == null || w.DataInvalid == false)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .Where(w => w.Meanings.Any(m => m.Definitions.Any()))
                .ToListAsync();

        private static string GetLevel(int xp) => xp switch
        {
            >= 5000 => "Master 👑",
            >= 2000 => "Advanced 🎯",
            >= 500 => "Explorer 🧭",
            >= 100 => "Learner 📖",
            _ => "Beginner 🌱"
        };
    }
}
