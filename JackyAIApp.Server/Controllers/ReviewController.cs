using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
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
        private const double MIN_EASE_FACTOR = 1.3;
        private const double DEFAULT_EASE_FACTOR = 2.5;

        private readonly ILogger<ReviewController> _logger;
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _dbContext;
        private readonly IUserService _userService;

        public ReviewController(
            ILogger<ReviewController> logger,
            IMyResponseFactory responseFactory,
            AzureSQLDBContext dbContext,
            IUserService userService)
        {
            _logger = logger;
            _responseFactory = responseFactory;
            _dbContext = dbContext;
            _userService = userService;
        }

        /// <summary>
        /// Get words due for review today (spaced repetition).
        /// Returns words where NextReviewDate is null (never reviewed) or before/on today.
        /// </summary>
        [HttpGet("due")]
        public async Task<IActionResult> GetDueReviews()
        {
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

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
                Meanings = uw.Word.Meanings.Select(m => new WordMeaning
                {
                    PartOfSpeech = m.PartOfSpeech,
                    Definitions = m.Definitions.Select(d => new Definition
                    {
                        English = d.English,
                        Chinese = d.Chinese
                    }).ToList(),
                    ExampleSentences = m.ExampleSentences.Select(e => new ExampleSentence
                    {
                        English = e.English,
                        Chinese = e.Chinese
                    }).ToList(),
                    Synonyms = m.Tags.Where(t => t.TagType == "Synonym").Select(t => t.Word).ToList(),
                    Antonyms = m.Tags.Where(t => t.TagType == "Antonym").Select(t => t.Word).ToList(),
                    RelatedWords = m.Tags.Where(t => t.TagType == "Related").Select(t => t.Word).ToList(),
                }).ToList(),
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
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

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

                int quality = Math.Clamp(review.Quality, 0, 5);
                userWord.ReviewCount++;

                if (quality >= 3)
                {
                    // Correct answer
                    correctCount++;
                    userWord.ConsecutiveCorrect++;

                    // SM-2: update ease factor
                    userWord.EaseFactor = Math.Max(
                        MIN_EASE_FACTOR,
                        userWord.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02))
                    );

                    // SM-2: update interval
                    if (userWord.ConsecutiveCorrect == 1)
                    {
                        userWord.ReviewIntervalDays = 1;
                    }
                    else if (userWord.ConsecutiveCorrect == 2)
                    {
                        userWord.ReviewIntervalDays = 6;
                    }
                    else
                    {
                        userWord.ReviewIntervalDays = Math.Round(userWord.ReviewIntervalDays * userWord.EaseFactor, 1);
                    }
                }
                else
                {
                    // Incorrect answer — reset
                    userWord.ConsecutiveCorrect = 0;
                    userWord.ReviewIntervalDays = 1;
                    // Ease factor still decreases on wrong answers
                    userWord.EaseFactor = Math.Max(
                        MIN_EASE_FACTOR,
                        userWord.EaseFactor - 0.2
                    );
                }

                userWord.NextReviewDate = today.AddDays(userWord.ReviewIntervalDays);
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
            var userId = _userService.GetUserId();
            if (userId == null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "User not found.");

            var today = DateTime.UtcNow.Date;
            var count = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId)
                .Where(uw => uw.NextReviewDate == null || uw.NextReviewDate <= today)
                .CountAsync();

            return _responseFactory.CreateOKResponse(new { dueCount = count });
        }
    }
}
