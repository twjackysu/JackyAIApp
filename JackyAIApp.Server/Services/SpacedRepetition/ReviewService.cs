using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Mappers;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.SpacedRepetition
{
    /// <summary>
    /// Reviewing the signed-in user's saved words on the SM-2 schedule
    /// (<see cref="ISpacedRepetitionService"/>). A word is due when it was never reviewed or its
    /// next review date is today (UTC) or earlier.
    /// </summary>
    public interface IReviewService
    {
        /// <summary>The words due now, never-reviewed first, at most a batch of them, with how many are due in all.</summary>
        Task<ServiceResult<DueReviewsResponse>> GetDueAsync(CancellationToken ct);

        /// <summary>Applies each answer to its word's schedule and awards XP for the correct ones.
        /// An answer for a word that is not the user's is skipped.</summary>
        Task<ServiceResult<ReviewSubmitResponse>> SubmitAsync(ReviewSubmitRequest request, CancellationToken ct);

        /// <summary>How many words are due now (for a badge).</summary>
        Task<ServiceResult<ReviewDueCountResponse>> GetDueCountAsync(CancellationToken ct);
    }

    public class ReviewService(
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        ISpacedRepetitionService srs,
        TimeProvider time) : IReviewService
    {
        private const int MaxReviewBatch = 20;
        private const int XpPerCorrectReview = 3;

        private DateTime Today => time.GetUtcNow().UtcDateTime.Date;

        private IQueryable<UserWord> Due(string userId, DateTime today) =>
            dbContext.UserWords.Where(uw => uw.UserId == userId && (uw.NextReviewDate == null || uw.NextReviewDate <= today));

        public async Task<ServiceResult<DueReviewsResponse>> GetDueAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var today = Today;

            var dueWords = await Due(userId, today)
                .OrderBy(uw => uw.NextReviewDate ?? DateTime.MinValue) // Never-reviewed first
                .Take(MaxReviewBatch)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.Definitions)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.ExampleSentences)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.Tags)
                .ToListAsync(ct);

            var totalDueCount = await Due(userId, today).CountAsync(ct);

            var items = dueWords.Select(uw => new ReviewWordItem
            {
                UserWordId = uw.Id,
                WordText = uw.Word.WordText,
                KKPhonics = uw.Word.KKPhonics,
                Meanings = WordMapper.ToMeaningDtos(uw.Word.Meanings),
                ReviewCount = uw.ReviewCount,
                ConsecutiveCorrect = uw.ConsecutiveCorrect,
            }).ToList();

            return ServiceResult.Ok(new DueReviewsResponse
            {
                DueWords = items,
                TotalDueCount = totalDueCount,
            });
        }

        public async Task<ServiceResult<ReviewSubmitResponse>> SubmitAsync(ReviewSubmitRequest request, CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            if (request.Reviews == null || request.Reviews.Count == 0)
                return ServiceResult.Fail(ErrorCodes.BadRequest, "No reviews provided.");

            var userWordIds = request.Reviews.Select(r => r.UserWordId).ToList();
            var userWords = await dbContext.UserWords
                .Where(uw => uw.UserId == userId && userWordIds.Contains(uw.Id))
                .ToListAsync(ct);

            int correctCount = 0;
            var today = Today;

            foreach (var review in request.Reviews)
            {
                var userWord = userWords.FirstOrDefault(uw => uw.Id == review.UserWordId);
                if (userWord == null) continue;

                if (srs.ApplyResult(userWord, review.Quality, today))
                {
                    correctCount++;
                }
            }

            int xpEarned = correctCount * XpPerCorrectReview;
            var user = await dbContext.Users.FindAsync([userId], ct);
            if (user != null)
            {
                user.TotalXP += xpEarned;
            }

            await dbContext.SaveChangesAsync(ct);

            return ServiceResult.Ok(new ReviewSubmitResponse
            {
                WordsReviewed = request.Reviews.Count,
                CorrectCount = correctCount,
                XPEarned = xpEarned,
            });
        }

        public async Task<ServiceResult<ReviewDueCountResponse>> GetDueCountAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var count = await Due(userId, Today).CountAsync(ct);
            return ServiceResult.Ok(new ReviewDueCountResponse(count));
        }
    }
}
