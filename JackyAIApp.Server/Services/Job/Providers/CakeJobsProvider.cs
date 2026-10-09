using JackyAIApp.Server.DTO.Job;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Job.Providers;

/// <summary>
/// Fetches job listings in Taiwan from Cake (cake.me, formerly CakeResume).
///
/// Uses the same JSON search API Cake's own frontend calls client-side:
///   POST https://api.cake.me/api/client/v2/jobs/search
///   { query, filters: { locations: ["Taiwan"] }, sort_by, page, per_page }
/// (sort_by is required; "popularity" | "latest".)
///
/// History: this used to scrape the __NEXT_DATA__ island off www.cake.me/jobs?q=...&location_list[0]=Taiwan.
/// Cake now 308-redirects any query/location URL to an SEO path (/jobs/{q}/in-Taiwan), and those
/// paths sit behind a Cloudflare challenge (403 "Just a moment...") — so the scrape returned zero
/// jobs for every search. The API host has no such challenge.
///
/// Results mix two shapes: Cake-hosted jobs (job_source "cake": path + page{path,name}) and
/// aggregated external postings (job_source "external": id + source_url + external_company{name}).
///
/// Engineering roles are kept via a title filter (Cake mixes all professions into one
/// result set), excluding business/finance/HR/sales/etc.
/// </summary>
public class CakeJobsProvider(IHttpClientFactory httpClientFactory, ILogger<CakeJobsProvider> logger) : IJobProvider
{
    public string PlatformId => "cake";

    private const string SearchUrl = "https://api.cake.me/api/client/v2/jobs/search";
    private const int PerPage = 30;
    private const int MaxPages = 2;

    private static readonly string[] IncludeKeywords =
    [
        "工程師", "engineer", "developer", "開發", "研發", "architect", "架構",
        "韌體", "firmware", "軟體", "software", "硬體", "hardware", "演算法", "algorithm",
        "ic設計", "verification", "驗證", "data scientist", "資料科學", "數據",
        "machine learning", "deep learning", " ml ", "sre", "devops", "系統", "system",
        "network", "網路", "security", "資安", "qa", "測試", "test", "automation",
        "自動化", "programmer", "程式", "frontend", "front-end", "backend", "back-end",
        "full stack", "full-stack", "前端", "後端", "全端",
    ];

    private static readonly string[] ExcludeKeywords =
    [
        "業務", "sales", "行銷", "marketing", "財務", "finance", "會計", "accounting",
        "人資", "人力資源", "recruit", "talent acquisition", "法務", "legal",
        "採購", "procurement", "供應鏈", "supply chain", "門市", "retail", "店員",
        "客服", "customer service", "行政", "總務", "公關", "intern", "實習",
    ];

    public async Task<IReadOnlyList<JobListingDto>> FetchAsync(string keywords, string searchStrategy, CancellationToken ct = default)
    {
        using var client = httpClientFactory.CreateClient("Cake");
        var jobs = new List<JobListingDto>();
        var seen = new HashSet<string>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var body = new
            {
                query = keywords?.Trim() ?? "",
                filters = new { locations = new[] { "Taiwan" } },
                sort_by = "latest",
                page,
                per_page = PerPage,
            };

            using var response = await client.PostAsJsonAsync(SearchUrl, body, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cake jobs page {page} returned {status}", page, response.StatusCode);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Cake jobs page {page} returned non-JSON body", page);
                break;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("Cake jobs page {page} response has no 'data' array", page);
                    break;
                }

                foreach (var entry in data.EnumerateArray())
                {
                    try
                    {
                        var job = ParseJob(entry);
                        if (job is null) continue;
                        if (seen.Add(job.Id)) jobs.Add(job);
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to parse Cake job"); }
                }

                var totalPages = root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var n) ? n : page;
                if (page >= totalPages || data.GetArrayLength() < PerPage) break; // last page
            }
        }

        logger.LogInformation("Cake fetched {count} engineering jobs for keywords='{kw}'", jobs.Count, keywords);
        return jobs.OrderByDescending(j => j.PostedAt).ToList();
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static JobListingDto? ParseJob(JsonElement job)
    {
        var title = GetString(job, "title") ?? "";
        if (string.IsNullOrWhiteSpace(title) || !IsEngineering(title)) return null;

        string id, url, companyName = "Cake";
        if (GetString(job, "job_source") == "external")
        {
            // Aggregated posting hosted on the employer's own career site.
            var externalId = GetString(job, "id") ?? "";
            if (job.TryGetProperty("external_company", out var ec) && ec.ValueKind == JsonValueKind.Object)
                companyName = GetString(ec, "name") ?? companyName;
            id = $"cake-ext-{externalId}";
            url = GetString(job, "source_url") ?? "https://www.cake.me/jobs";
        }
        else
        {
            var path = GetString(job, "path") ?? "";
            var companySlug = "";
            if (job.TryGetProperty("page", out var pageEl) && pageEl.ValueKind == JsonValueKind.Object)
            {
                companySlug = GetString(pageEl, "path") ?? "";
                companyName = GetString(pageEl, "name") ?? companyName;
            }
            id = $"cake-{companySlug}-{path}";
            url = !string.IsNullOrEmpty(companySlug) && !string.IsNullOrEmpty(path)
                ? $"https://www.cake.me/companies/{companySlug}/jobs/{path}"
                : "https://www.cake.me/jobs";
        }

        // Location: join the localized location list.
        var location = "台灣";
        if (job.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array)
        {
            var names = locs.EnumerateArray()
                .Where(l => l.ValueKind == JsonValueKind.String)
                .Select(l => l.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            if (names.Length > 0) location = string.Join(", ", names);
        }

        var salaryRange = FormatSalary(job);

        var postedAt = DateTime.UtcNow;
        if (GetString(job, "content_updated_at") is { } uaStr &&
            DateTime.TryParse(uaStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            postedAt = parsed;

        var tags = new List<string>();
        if (job.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            foreach (var tg in tagsEl.EnumerateArray())
                if (tg.ValueKind == JsonValueKind.String && tg.GetString() is { Length: > 0 } tagStr) tags.Add(tagStr);
        if (GetString(job, "seniority_level") is { } slStr)
            tags.Add(slStr);

        var description = GetString(job, "description") is { } dStr
            ? dStr[..Math.Min(200, dStr.Length)].Trim()
            : null;

        return new JobListingDto(
            Id: id,
            Title: title,
            Company: companyName,
            Location: location,
            SalaryRange: salaryRange,
            Url: url,
            PostedAt: postedAt,
            Platform: "Cake",
            Tags: [.. tags],
            Description: description
        );
    }

    private static string? FormatSalary(JsonElement job)
    {
        if (!job.TryGetProperty("salary", out var s) || s.ValueKind != JsonValueKind.Object) return null;

        string? Get(string name) => s.TryGetProperty(name, out var el)
            ? el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => el.ToString(),
            }
            : null;

        var min = Get("min");
        var max = Get("max");
        var currency = Get("currency") ?? "";
        var type = Get("type") switch
        {
            "per_year" => "/年",
            "per_month" => "/月",
            "per_hour" => "/時",
            _ => ""
        };

        if (string.IsNullOrEmpty(min) && string.IsNullOrEmpty(max)) return null;

        static string Fmt(string? v) => long.TryParse(v, out var n) ? n.ToString("N0") : v ?? "";
        var amount = !string.IsNullOrEmpty(max) && max != min
            ? $"{Fmt(min)} - {Fmt(max)}"
            : Fmt(min);
        return $"{currency} {amount}{type}".Trim();
    }

    private static bool IsEngineering(string title)
    {
        if (ExcludeKeywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase))) return false;
        return IncludeKeywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
