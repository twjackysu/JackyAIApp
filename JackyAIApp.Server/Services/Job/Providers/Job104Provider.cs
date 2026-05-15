using JackyAIApp.Server.DTO.Job;
using System.Text.Json;
using System.Web;

namespace JackyAIApp.Server.Services.Job.Providers;

/// <summary>
/// Fetches job listings from 104.com.tw via its unofficial (but stable) JSON search API.
/// area=6001001000 = 全台灣; order=1, asc=0 = sorted by date desc
/// </summary>
public class Job104Provider(IHttpClientFactory httpClientFactory, ILogger<Job104Provider> logger) : IJobProvider
{
    public string PlatformId => "104";

    // Fetch up to this many pages (32 jobs/page). 3 pages ≈ last few days of postings.
    private const int MaxPages = 3;

    private const string SearchUrlTemplate =
        "https://www.104.com.tw/jobs/search/api/jobs" +
        "?area=6001001000&order=1&asc=0&ro=0&mode=s&jobsource=2018indexpoc&page={0}&keyword={1}";

    public async Task<IReadOnlyList<JobListingDto>> FetchAsync(string keywords, string searchStrategy, CancellationToken ct = default)
    {
        var keywordParam = BuildKeywordParam(keywords, searchStrategy);
        var encoded = HttpUtility.UrlEncode(keywordParam);

        using var client = httpClientFactory.CreateClient("104");
        var jobs = new List<JobListingDto>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var url = string.Format(SearchUrlTemplate, page, encoded);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) break;

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            // API v2: root.data is a JSON array directly (not data.list)
            if (!root.TryGetProperty("data", out var dataEl)) break;

            var listEl = dataEl.ValueKind == JsonValueKind.Array
                ? dataEl                                    // new format: data is the array
                : dataEl.TryGetProperty("list", out var nested) ? nested : default; // legacy

            if (listEl.ValueKind != JsonValueKind.Array) break;

            var pageJobs = new List<JobListingDto>();
            foreach (var item in listEl.EnumerateArray())
            {
                try { pageJobs.Add(ParseJob(item)); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to parse 104 job item on page {page}", page); }
            }

            jobs.AddRange(pageJobs);

            // Stop early if this page had fewer items than expected (last page)
            if (pageJobs.Count < 30) break;

            // Check pagination metadata to avoid unnecessary requests
            if (root.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("pagination", out var pag) &&
                pag.TryGetProperty("lastPage", out var lastPageEl) &&
                lastPageEl.TryGetInt32(out var lastPage) &&
                page >= lastPage)
                break;
        }

        logger.LogInformation("104 fetched {count} jobs for keywords='{kw}'", jobs.Count, keywords);
        return jobs;
    }

    private static string BuildKeywordParam(string keywords, string strategy)
    {
        // 104 uses space-separated keywords as AND by default
        // For OR: use comma-separated
        var terms = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return strategy.Equals("OR", StringComparison.OrdinalIgnoreCase)
            ? string.Join(",", terms)
            : string.Join(" ", terms);
    }

    private static JobListingDto ParseJob(JsonElement item)
    {
        var jobNo = item.GetProperty("jobNo").GetString() ?? "";
        var jobName = item.GetProperty("jobName").GetString() ?? "";
        var custName = item.GetProperty("custName").GetString() ?? "";

        // Location: prefer jobAddrNoDesc (human-readable), fallback to jobAddress
        var location = item.TryGetProperty("jobAddrNoDesc", out var addrDesc) && addrDesc.GetString() is { Length: > 0 } desc
            ? desc
            : item.TryGetProperty("jobAddress", out var addr) ? addr.GetString() ?? "" : "";

        // Salary
        string? salaryRange = null;
        if (item.TryGetProperty("salaryDesc", out var salaryDescEl) && salaryDescEl.GetString() is { Length: > 0 } sd)
            salaryRange = sd;

        // Date: "20260508" → DateTime
        var postedAt = DateTime.UtcNow;
        if (item.TryGetProperty("appearDate", out var dateEl) && dateEl.GetString() is { Length: 8 } dateStr)
        {
            if (DateTime.TryParseExact(dateStr, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                postedAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        // URL
        var url = item.TryGetProperty("link", out var linkEl) &&
                  linkEl.TryGetProperty("job", out var jobLinkEl) &&
                  jobLinkEl.GetString() is { Length: > 0 } jobLink
            ? jobLink
            : $"https://www.104.com.tw/job/{jobNo}";

        // Tags
        var tags = new List<string>();
        if (item.TryGetProperty("tags", out var tagsEl))
        {
            foreach (var tag in tagsEl.EnumerateArray())
            {
                if (tag.TryGetProperty("name", out var tagName) && tagName.GetString() is { Length: > 0 } tn)
                    tags.Add(tn);
                else if (tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } ts)
                    tags.Add(ts);
            }
        }

        // Description snippet
        var description = item.TryGetProperty("descSnippet", out var descEl) ? descEl.GetString() : null;

        return new JobListingDto(
            Id: $"104-{jobNo}",
            Title: jobName,
            Company: custName,
            Location: location,
            SalaryRange: salaryRange,
            Url: url,
            PostedAt: postedAt,
            Platform: "104人力銀行",
            Tags: [.. tags],
            Description: description
        );
    }
}
