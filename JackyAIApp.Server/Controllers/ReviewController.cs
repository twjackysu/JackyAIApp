using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Mappers;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Spaced repetition review controller using SM-2 algorithm.
    /// Words in the user's repository are scheduled for review based on how well they know them.
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ReviewController : ControllerBase
    {
        private const int MAX_REVIEW_BATCH = 20;

        private readonly ILogger<ReviewController> _logger;
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _dbContext;
        private readonly ICurrentUser _currentUser;
        private readonly ISpacedRepetitionService _srs;

        public ReviewController(
            ILogger<ReviewController> logger,
            IMyResponseFactory responseFactory,
            AzureSQLDBContext dbContext,
            ICurrentUser currentUser,
            ISpacedRepetitionService srs)
        {
            _logger = logger;
            _responseFactory = responseFactory;
            _dbContext = dbContext;
            _currentUser = currentUser;
            _srs = srs;
        }

        /// <summary>
        /// Get words due for review today (spaced repetition).
        /// Returns words where NextReviewDate is null (never reviewed) or before/on today.
        /// </summary>
        [HttpGet("due")]
        public async Task<IActionResult> GetDueReviews()
        {
            var userId = _currentUser.RequireId();
            var today = DateTime.UtcNow.Date;

            var dueWords = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId)
                .Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today)
                .OrderBy(uw => uw.NextReviewDate ?? DateTime.MinValue) // Never-reviewed first
                .Take(MAX_REVIEW_BATCH)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.Definitions)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.ExampleSentences)
                .Include(uw => uw.Word)
                    .ThenInclude(w => w.Meanings)
                        .ThenInclude(m => m.Tags)
                .ToListAsync();

            var totalDueCount = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId)
                .Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today)
                .CountAsync();

            var items = dueWords.Select(uw => new ReviewWordItem
            {
                UserWordId = uw.Id,
                WordText = uw.Word.WordText,
                KKPhonics = uw.Word.KKPhonics,
                Meanings = WordMapper.ToMeaningDtos(uw.Word.Meanings),
                ReviewCount = uw.ReviewCount,
                ConsecutiveCorrect = uw.ConsecutiveCorrect,
            }).ToList();

            return _responseFactory.CreateOKResponse(new DueReviewsResponse
            {
                DueWords = items,
                TotalDueCount = totalDueCount,
            });
        }

        /// <summary>
        /// Submit review results. Updates SM-2 scheduling for each word.
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitReviews([FromBody] ReviewSubmitRequest request)
        {
            var userId = _currentUser.RequireId();
            if (request.Reviews == null || !request.Reviews.Any())
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No reviews provided.");

            var userWordIds = request.Reviews.Select(r => r.UserWordId).ToList();
            var userWords = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId && userWordIds.Contains(uw.Id))
                .ToListAsync();

            int correctCount = 0;
            var today = DateTime.UtcNow.Date;

            foreach (var review in request.Reviews)
            {
                var userWord = userWords.FirstOrDefault(uw => uw.Id == review.UserWordId);
                if (userWord == null) continue;

                if (_srs.ApplyResult(userWord, review.Quality, today))
                {
                    correctCount++;
                }
            }

            // Award XP: 3 XP per correct review
            int xpEarned = correctCount * 3;
            var user = await _dbContext.Users.FindAsync(userId);
            if (user != null)
            {
                user.TotalXP += xpEarned;
            }

            await _dbContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse(new ReviewSubmitResponse
            {
                WordsReviewed = request.Reviews.Count,
                CorrectCount = correctCount,
                XPEarned = xpEarned,
            });
        }

        /// <summary>
        /// Get count of words due for review (for badge/notification purposes).
        /// </summary>
        [HttpGet("count")]
        public async Task<IActionResult> GetDueCount()
        {
            var userId = _currentUser.RequireId();
            var today = DateTime.UtcNow.Date;
            var count = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId)
                .Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today)
                .CountAsync();

            return _responseFactory.CreateOKResponse(new { dueCount = count });
        }
    }
}
