using JackyAIApp.Server.DTO.Job;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Web;

namespace JackyAIApp.Server.Services.Job.Providers;

/// <summary>
/// Fetches job listings in Taiwan from Cake (cake.me, formerly CakeResume).
///
/// Cake is a Next.js site with no bot-gating (plain server-side requests return 200),
/// and it server-renders the job list into the __NEXT_DATA__ island at
///   props.pageProps.initialState.jobSearch.entityByPathId
/// Search URL: https://www.cake.me/jobs?q={keyword}&location_list[0]=Taiwan&page={n}
///
/// Engineering roles are kept via a title filter (Cake mixes all professions into one
/// result set), excluding business/finance/HR/sales/etc.
/// </summary>
public class CakeJobsProvider(IHttpClientFactory httpClientFactory, ILogger<CakeJobsProvider> logger) : IJobProvider
{
    public string PlatformId => "cake";

    private const int MaxPages = 3; // 10 jobs/page

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
            var url = new StringBuilder("https://www.cake.me/jobs?location_list%5B0%5D=Taiwan");
            url.Append("&page=").Append(page);
            if (!string.IsNullOrWhiteSpace(keywords))
                url.Append("&q=").Append(HttpUtility.UrlEncode(keywords.Trim()));

            using var response = await client.GetAsync(url.ToString(), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cake jobs page {page} returned {status}", page, response.StatusCode);
                break;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var nextJson = ExtractNextData(html);
            if (nextJson is null) break;

            using var doc = JsonDocument.Parse(nextJson);
            if (!TryGetJobMap(doc.RootElement, out var jobMap)) break;

            var pageCount = 0;
            foreach (var entry in jobMap.EnumerateObject())
            {
                pageCount++;
                try
                {
                    var job = ParseJob(entry.Value);
                    if (job is null) continue;
                    if (seen.Add(job.Id)) jobs.Add(job);
                }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to parse Cake job"); }
            }

            if (pageCount < 10) break; // last page
        }

        logger.LogInformation("Cake fetched {count} engineering jobs for keywords='{kw}'", jobs.Count, keywords);
        return jobs.OrderByDescending(j => j.PostedAt).ToList();
    }

    private static string? ExtractNextData(string html)
    {
        const string marker = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return end < 0 ? null : html[start..end];
    }

    private static bool TryGetJobMap(JsonElement root, out JsonElement jobMap)
    {
        jobMap = default;
        if (root.TryGetProperty("props", out var props) &&
            props.TryGetProperty("pageProps", out var pageProps) &&
            pageProps.TryGetProperty("initialState", out var initialState) &&
            initialState.TryGetProperty("jobSearch", out var jobSearch) &&
            jobSearch.TryGetProperty("entityByPathId", out var map) &&
            map.ValueKind == JsonValueKind.Object)
        {
            jobMap = map;
            return true;
        }
        return false;
    }

    private static JobListingDto? ParseJob(JsonElement job)
    {
        var title = job.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(title) || !IsEngineering(title)) return null;

        var path = job.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";

        string companySlug = "", companyName = "Cake";
        if (job.TryGetProperty("page", out var pageEl) && pageEl.ValueKind == JsonValueKind.Object)
        {
            companySlug = pageEl.TryGetProperty("path", out var cs) ? cs.GetString() ?? "" : "";
            companyName = pageEl.TryGetProperty("name", out var cn) && cn.GetString() is { Length: > 0 } n ? n : companyName;
        }

        var url = !string.IsNullOrEmpty(companySlug) && !string.IsNullOrEmpty(path)
            ? $"https://www.cake.me/companies/{companySlug}/jobs/{path}"
            : "https://www.cake.me/jobs";

        // Location: join the localized location list.
        var location = "台灣";
        if (job.TryGetProperty("locations", out var locs) && locs.ValueKind == JsonValueKind.Array)
        {
            var names = locs.EnumerateArray().Select(l => l.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (names.Length > 0) location = string.Join(", ", names);
        }

        var salaryRange = FormatSalary(job);

        var postedAt = DateTime.UtcNow;
        if (job.TryGetProperty("contentUpdatedAt", out var ua) && ua.GetString() is { Length: > 0 } uaStr &&
            DateTime.TryParse(uaStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            postedAt = parsed;

        var tags = new List<string>();
        if (job.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
            foreach (var tg in tagsEl.EnumerateArray())
                if (tg.GetString() is { Length: > 0 } tagStr) tags.Add(tagStr);
        if (job.TryGetProperty("seniorityLevel", out var sl) && sl.GetString() is { Length: > 0 } slStr)
            tags.Add(slStr);

        var description = job.TryGetProperty("description", out var d) && d.GetString() is { Length: > 0 } dStr
            ? dStr[..Math.Min(200, dStr.Length)].Trim()
            : null;

        return new JobListingDto(
            Id: $"cake-{companySlug}-{path}",
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
            ? (el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString())
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
