using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class DailyChallengeController(
        IMyResponseFactory responseFactory,
        IDailyChallengeService dailyChallengeService) : ControllerBase
    {
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly IDailyChallengeService _dailyChallengeService = dailyChallengeService;

        /// <summary>Get today's daily challenge. Same questions for all users on the same day (date-seeded).</summary>
        [HttpGet]
        public async Task<IActionResult> GetChallenge() =>
            ToActionResult(await _dailyChallengeService.GetChallengeAsync());

        /// <summary>Submit answers for today's daily challenge.</summary>
        [HttpPost("submit")]
        public async Task<IActionResult> SubmitChallenge([FromBody] DailyChallengeSubmitRequest request) =>
            ToActionResult(await _dailyChallengeService.SubmitChallengeAsync(request));

        /// <summary>Get user's daily challenge stats (streak, XP, level).</summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats() =>
            ToActionResult(await _dailyChallengeService.GetStatsAsync());

        /// <summary>Claim daily login bonus credits (10 credits, once per day).</summary>
        [HttpPost("claim-daily-bonus")]
        public async Task<IActionResult> ClaimDailyBonus() =>
            ToActionResult(await _dailyChallengeService.ClaimDailyBonusAsync());

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
