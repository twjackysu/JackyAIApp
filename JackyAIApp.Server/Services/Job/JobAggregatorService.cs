using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Job;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Job;

public class JobAggregatorService(
    IEnumerable<IJobProvider> providers,
    IMemoryCache cache,
    ILogger<JobAggregatorService> logger) : IJobAggregatorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<JobListingDto>> SearchAsync(JobSearchRequest request, CancellationToken ct = default)
    {
        var normalizedKeywords = request.Keywords.Trim().ToLowerInvariant();
        var platformKey = string.Join(",", request.Platforms.OrderBy(p => p));
        var cacheKey = $"jobs:{normalizedKeywords}:{request.SearchStrategy}:{platformKey}";

        var selectedProviders = providers
            .Where(p => request.Platforms.Contains(p.PlatformId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (selectedProviders.Count == 0)
        {
            logger.LogWarning("No matching providers for platforms: {platforms}", string.Join(",", request.Platforms));
            return [];
        }

        // The same search from two people at once shares one round of provider calls.
        return await cache.GetOrCreateSingleFlightAsync(cacheKey, CacheDuration, async () =>
        {
            // Shared by every caller waiting on it, so no one caller's token may stop it.
            // Fetch from all selected providers in parallel
            var tasks = selectedProviders.Select(p => FetchSafe(p, request.Keywords, request.SearchStrategy, CancellationToken.None));
            var results = await Task.WhenAll(tasks);

            var merged = results
                .SelectMany(r => r)
                .DistinctBy(j => j.Id)                  // dedup by platform-scoped ID
                .OrderByDescending(j => j.PostedAt)      // newest first
                .ToList();

            logger.LogInformation("Job search completed: {count} results for '{keywords}'", merged.Count, request.Keywords);
            return (IReadOnlyList<JobListingDto>)merged;
        }, ct);
    }

    private async Task<IReadOnlyList<JobListingDto>> FetchSafe(
        IJobProvider provider, string keywords, string strategy, CancellationToken ct)
    {
        try
        {
            return await provider.FetchAsync(keywords, strategy, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provider {platform} failed", provider.PlatformId);
            return [];
        }
    }
}
