using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Job;
using JackyAIApp.Server.Services.Job;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class JobController(
    IJobAggregatorService aggregator,
    IMyResponseFactory responseFactory,
    ILogger<JobController> logger) : ControllerBase
{
    private static readonly string[] DefaultPlatforms =
        ["104", "github-f2etw", "github-golangtw", "github-devopstw"];

    /// <summary>
    /// Search job listings across multiple platforms.
    /// Results are sorted by post date (newest first) and cached for 30 minutes.
    /// </summary>
    [HttpGet("listings")]
    public async Task<IActionResult> GetListings(
        [FromQuery] string keywords = "software engineer",
        [FromQuery] string strategy = "AND",
        [FromQuery] string? platforms = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Keywords cannot be empty.");

        var strategyNorm = strategy.Equals("OR", StringComparison.OrdinalIgnoreCase) ? "OR" : "AND";

        var platformList = platforms is { Length: > 0 }
            ? platforms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultPlatforms;

        var request = new JobSearchRequest(keywords.Trim(), strategyNorm, platformList);

        logger.LogInformation("Job search: keywords='{keywords}' strategy={strategy} platforms={platforms}",
            request.Keywords, request.SearchStrategy, string.Join(",", request.Platforms));

        var results = await aggregator.SearchAsync(request, ct);
        return responseFactory.CreateOKResponse(results);
    }

    /// <summary>
    /// Returns the list of supported platform IDs and their display names.
    /// </summary>
    [HttpGet("platforms")]
    public IActionResult GetPlatforms()
    {
        var platforms = new[]
        {
            new { Id = "104",              DisplayName = "104人力銀行" },
            new { Id = "github-f2etw",    DisplayName = "GitHub · f2etw/jobs (前端台灣)" },
            new { Id = "github-golangtw", DisplayName = "GitHub · golangTW/jobs (Golang 台灣)" },
            new { Id = "github-devopstw", DisplayName = "GitHub · DevOpsTW/jobs (DevOps 台灣)" },
        };
        return responseFactory.CreateOKResponse(platforms);
    }
}
