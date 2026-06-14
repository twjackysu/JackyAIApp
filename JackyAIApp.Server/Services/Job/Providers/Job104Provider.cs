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

    /// <summary>
    /// Builds a human-readable salary string from a 104 job item.
    /// The v2 search API exposes numeric <c>salaryLow</c>/<c>salaryHigh</c> plus a
    /// <c>salaryType</c> code (10=面議, 30=時薪, 50=月薪, 60=年薪, 70=部分工時), and no
    /// longer includes the old <c>salaryDesc</c> string, so we derive it ourselves.
    /// Returns null only when nothing usable is present.
    /// </summary>
    private static string? BuildSalaryRange(JsonElement item)
    {
        string? typeLabel = null;
        if (item.TryGetProperty("salaryType", out var typeEl))
        {
            var typeCode = typeEl.ValueKind == JsonValueKind.Number
                ? typeEl.GetInt32().ToString()
                : typeEl.GetString();
            switch (typeCode)
            {
                case "10": return "面議";          // negotiable
                case "30": typeLabel = "時薪"; break; // hourly
                case "50": typeLabel = "月薪"; break; // monthly
                case "60": typeLabel = "年薪"; break; // yearly
                case "70": typeLabel = "月薪"; break; // part-time (monthly)
            }
        }

        // Prefer a ready-made description if the API happens to provide one.
        if (item.TryGetProperty("salaryDesc", out var descEl) &&
            descEl.GetString() is { Length: > 0 } desc && desc != "待遇面議")
            return desc;

        var low = ReadLong(item, "salaryLow");
        var high = ReadLong(item, "salaryHigh");

        // 104 uses a large sentinel (e.g. 9,999,999) for the open-ended upper bound.
        string range;
        if (low > 0 && high > 0 && high < 9_999_999)
            range = $"{low:N0} - {high:N0} 元";
        else if (low > 0)
            range = $"{low:N0} 元以上";
        else
            return "面議";

        return typeLabel is { Length: > 0 } ? $"{typeLabel} {range}" : range;
    }

    private static long ReadLong(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var el)) return 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(el.GetString(), out var n) => n,
            _ => 0,
        };
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
        var salaryRange = BuildSalaryRange(item);

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

        // Tags: prefer pcSkills[].description (human-readable skill names).
        // The legacy "tags" field changed from array → dict in API v2 and desc values are
        // now empty, so it is no longer useful as a tag source.
        var tags = new List<string>();
        if (item.TryGetProperty("pcSkills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var skill in skillsEl.EnumerateArray())
            {
                if (skill.TryGetProperty("description", out var skillDesc) &&
                    skillDesc.GetString() is { Length: > 0 } sn)
                    tags.Add(sn);
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
