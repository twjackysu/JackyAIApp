using JackyAIApp.Server.DTO.Job;
using System.Text.Json;
using System.Web;

namespace JackyAIApp.Server.Services.Job.Providers;

/// <summary>
/// Fetches job postings from Taiwan community GitHub Issues job boards.
/// 
/// Supported repos (PlatformId → repo):
///   github-f2etw   → f2etw/jobs      (Frontend TW, most active, ⭐1880)
///   github-golangtw → golangTW/jobs   (Golang TW, ⭐749)
///   github-devopstw → DevOpsTW/jobs   (DevOps TW, ⭐69)
/// 
/// Strategy: fetch latest 50 open issues, then filter by keywords client-side.
/// Uses GitHub Issues API (5000 req/hr with auth, 60 req/hr anonymous).
/// </summary>
public class GitHubJobsProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubJobsProvider> logger,
    string platformId,
    string owner,
    string repo,
    string displayName) : IJobProvider
{
    public string PlatformId => platformId;

    private static readonly string[] JobLabels = ["求才", "徵才", "hiring", "job"];

    public async Task<IReadOnlyList<JobListingDto>> FetchAsync(string keywords, string searchStrategy, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/issues" +
                  "?state=open&sort=created&direction=desc&per_page=50";

        using var client = httpClientFactory.CreateClient("GitHub");
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var terms = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(t => t.ToLowerInvariant())
                            .ToArray();

        var jobs = new List<JobListingDto>();
        foreach (var issue in doc.RootElement.EnumerateArray())
        {
            // Skip pull requests (GitHub API returns PRs in issues endpoint)
            if (issue.TryGetProperty("pull_request", out _)) continue;

            var title = issue.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
            var body = issue.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            var combined = (title + " " + body).ToLowerInvariant();

            // Keyword filter
            bool matches = searchStrategy.Equals("OR", StringComparison.OrdinalIgnoreCase)
                ? terms.Any(t => combined.Contains(t))
                : terms.All(t => combined.Contains(t));

            if (!matches && terms.Length > 0) continue;

            try { jobs.Add(ParseIssue(issue, title, body)); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to parse GitHub issue in {owner}/{repo}", owner, repo); }
        }

        return jobs;
    }

    private JobListingDto ParseIssue(JsonElement issue, string title, string body)
    {
        var number = issue.GetProperty("number").GetInt32();
        var htmlUrl = issue.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var createdAt = issue.TryGetProperty("created_at", out var dateEl) && dateEl.GetString() is { } ds
            ? DateTime.Parse(ds, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : DateTime.UtcNow;

        // Labels
        var labels = new List<string>();
        if (issue.TryGetProperty("labels", out var labelsEl))
        {
            foreach (var lbl in labelsEl.EnumerateArray())
            {
                if (lbl.TryGetProperty("name", out var lblName) && lblName.GetString() is { Length: > 0 } ln)
                    labels.Add(ln);
            }
        }

        // Try to extract company and location from common title patterns:
        // [徵才][台北][100-150K] 某公司 - Senior Engineer
        // [徵才] Company Name - Job Title
        var (company, location, salaryRange, cleanTitle) = ParseTitlePattern(title);

        // If no body snippet, take first 200 chars of body
        var description = body.Length > 0 ? body[..Math.Min(200, body.Length)].Trim() : null;

        return new JobListingDto(
            Id: $"{platformId}-{number}",
            Title: cleanTitle,
            Company: company,
            Location: location.Length > 0 ? location : "台灣",
            SalaryRange: salaryRange,
            Url: htmlUrl,
            PostedAt: createdAt,
            Platform: displayName,
            Tags: [.. labels],
            Description: description
        );
    }

    /// <summary>
    /// Parses common GitHub job issue title patterns used by Taiwan community repos.
    /// 
    /// Patterns:
    ///   [徵才][台北][100-150K] 公司名稱 - 職位名稱
    ///   [徵才][台北] 公司 招募 職位
    ///   [求才] 職位名稱 @ 公司
    ///   Software Engineer - Company Name
    /// </summary>
    private static (string Company, string Location, string? Salary, string Title) ParseTitlePattern(string rawTitle)
    {
        var title = rawTitle.Trim();
        string location = "";
        string? salary = null;

        // Strip bracket tags and capture location/salary info
        // Pattern: [tag][location][salary] or [tag][location]
        var bracketRegex = System.Text.RegularExpressions.Regex.Matches(title, @"\[([^\]]+)\]");
        var remaining = System.Text.RegularExpressions.Regex.Replace(title, @"\[[^\]]+\]", "").Trim();

        foreach (System.Text.RegularExpressions.Match m in bracketRegex)
        {
            var val = m.Groups[1].Value.Trim();
            // Location heuristics: contains city keywords
            if (val.Contains("台北") || val.Contains("台中") || val.Contains("高雄") ||
                val.Contains("新竹") || val.Contains("遠端") || val.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
                val.Contains("台灣") || val.Contains("全台"))
            {
                location = val;
            }
            // Salary heuristics: contains numbers + K or 萬 or salary keywords
            else if (System.Text.RegularExpressions.Regex.IsMatch(val, @"\d") &&
                     (val.Contains("K") || val.Contains("k") || val.Contains("萬") ||
                      val.Contains("薪") || val.Contains("~") || val.Contains("-")))
            {
                salary = val;
            }
        }

        // Try to split company from title: "Company - Title" or "Title @ Company"
        string company = "";
        if (remaining.Contains(" - "))
        {
            var parts = remaining.Split(" - ", 2);
            // Heuristic: shorter part is likely company
            if (parts[0].Length < parts[1].Length)
            {
                company = parts[0].Trim();
                remaining = parts[1].Trim();
            }
            else
            {
                company = parts[1].Trim();
                remaining = parts[0].Trim();
            }
        }
        else if (remaining.Contains(" @ "))
        {
            var parts = remaining.Split(" @ ", 2);
            remaining = parts[0].Trim();
            company = parts[1].Trim();
        }

        return (company, location, salary, remaining.Length > 0 ? remaining : rawTitle);
    }
}

/// <summary>
/// Factory that produces pre-configured GitHubJobsProvider instances.
/// Each supported repo gets its own registration via named provider.
/// </summary>
public static class GitHubJobsProviderFactory
{
    public static IJobProvider F2etw(IHttpClientFactory factory, ILogger<GitHubJobsProvider> logger)
        => new GitHubJobsProvider(factory, logger, "github-f2etw", "f2etw", "jobs", "GitHub · f2etw/jobs (前端台灣)");

    public static IJobProvider GolangTW(IHttpClientFactory factory, ILogger<GitHubJobsProvider> logger)
        => new GitHubJobsProvider(factory, logger, "github-golangtw", "golangTW", "jobs", "GitHub · golangTW/jobs (Golang 台灣)");

    public static IJobProvider DevOpsTW(IHttpClientFactory factory, ILogger<GitHubJobsProvider> logger)
        => new GitHubJobsProvider(factory, logger, "github-devopstw", "DevOpsTW", "jobs", "GitHub · DevOpsTW/jobs (DevOps 台灣)");
}
