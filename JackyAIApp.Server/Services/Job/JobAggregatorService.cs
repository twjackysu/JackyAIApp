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

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<JobListingDto>? cached) && cached is not null)
        {
            logger.LogInformation("Job search cache hit for key: {key}", cacheKey);
            return cached;
        }

        var selectedProviders = providers
            .Where(p => request.Platforms.Contains(p.PlatformId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (selectedProviders.Count == 0)
        {
            logger.LogWarning("No matching providers for platforms: {platforms}", string.Join(",", request.Platforms));
            return [];
        }

        // Fetch from all selected providers in parallel
        var tasks = selectedProviders.Select(p => FetchSafe(p, request.Keywords, request.SearchStrategy, ct));
        var results = await Task.WhenAll(tasks);

        var merged = results
            .SelectMany(r => r)
            .DistinctBy(j => j.Id)                  // dedup by platform-scoped ID
            .OrderByDescending(j => j.PostedAt)      // newest first
            .ToList();

        cache.Set(cacheKey, (IReadOnlyList<JobListingDto>)merged, CacheDuration);
        logger.LogInformation("Job search completed: {count} results for '{keywords}'", merged.Count, request.Keywords);

        return merged;
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
