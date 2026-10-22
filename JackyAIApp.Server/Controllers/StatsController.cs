using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.DailyChallenge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class StatsController(IMyResponseFactory responseFactory, IStatsService stats) : ControllerBase
    {
        /// <summary>
        /// Get the user's weekly learning report.
        /// </summary>
        [HttpGet("weekly-report")]
        public async Task<IActionResult> GetWeeklyReport(CancellationToken ct = default) =>
            responseFactory.ToActionResult(await stats.GetWeeklyReportAsync(ct));

        /// <summary>
        /// Get the leaderboard (top users by XP).
        /// </summary>
        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetLeaderboard([FromQuery] int limit = 20, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await stats.GetLeaderboardAsync(limit, ct));

        /// <summary>
        /// Per-day learning activity for a calendar heatmap (challenges + exam attempts).
        /// </summary>
        [HttpGet("activity")]
        public async Task<IActionResult> GetActivity([FromQuery] int days = 365, CancellationToken ct = default) =>
            responseFactory.ToActionResult(await stats.GetActivityAsync(days, ct));
    }
}
