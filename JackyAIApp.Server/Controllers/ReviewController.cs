using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.SpacedRepetition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Spaced repetition review controller using SM-2 algorithm.
    /// Words in the user's repository are scheduled for review based on how well they know them.
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ReviewController(IMyResponseFactory responseFactory, IReviewService reviews) : ControllerBase
    {
        /// <summary>
        /// Get words due for review today (spaced repetition).
        /// Returns words where NextReviewDate is null (never reviewed) or before/on today.
        /// </summary>
        [HttpGet("due")]
        public async Task<IActionResult> GetDueReviews(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await reviews.GetDueAsync(ct));

        /// <summary>
        /// Submit review results. Updates SM-2 scheduling for each word.
        /// </summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitReviews([FromBody] ReviewSubmitRequest request, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await reviews.SubmitAsync(request, ct));

        /// <summary>
        /// Get count of words due for review (for badge/notification purposes).
        /// </summary>
        [HttpGet("count")]
        public async Task<IActionResult> GetDueCount(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await reviews.GetDueCountAsync(ct));
    }
}
