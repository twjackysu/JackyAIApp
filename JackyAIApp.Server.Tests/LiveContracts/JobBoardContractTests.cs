using System.Net.Http.Json;
using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for the two job-board integrations behind the /jobs feature
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C, C1/C2). Both are unofficial, undocumented JSON
    /// APIs (the ones each site's own frontend calls) with no SLA — the highest-value thing this class watches for is
    /// "the site stopped answering our shape of request at all", not just "a field got renamed".
    ///
    /// These build their own HttpClient with the same headers Job104Provider/CakeJobsProvider get
    /// from their named DI clients ("104"/"Cake" in ServiceCollectionExtensions.cs) — contract
    /// tests intentionally bypass DI (see LiveContractBase) so they're exercising the third
    /// party's actual shape, not our wiring.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class JobBoardContractTests
    {
        private const string SearchKeyword = "工程師"; // common enough to reliably return results on both sites

        // ── C1: 104 ──────────────────────────────────────────────────────────────────────────

        private static HttpClient CreateJob104Client()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // Mirrors the "104" named HttpClient registration in ServiceCollectionExtensions.cs —
            // 104's search API is unofficial and has been observed to behave differently (or
            // reject outright) without these.
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.104.com.tw/");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            return client;
        }

        [Fact]
        public async Task Job104Search_KnownKeyword_ReturnsParsableJobsWithRequiredFields()
        {
            using var client = CreateJob104Client();
            var encoded = Uri.EscapeDataString(SearchKeyword);
            var url = "https://www.104.com.tw/jobs/search/api/jobs" +
                      $"?area=6001001000&order=1&asc=0&ro=0&mode=s&jobsource=2018indexpoc&page=1&keyword={encoded}";

            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode,
                $"104 search API request failed: HTTP {(int)response.StatusCode} {response.StatusCode}. " +
                "If this is a 403 with a Cloudflare 'Just a moment...' challenge page in the body, 104 has " +
                "tightened bot detection beyond the Referer/User-Agent headers Job104Provider sends — this is a " +
                "genuine break in the integration (confirmed reproducible with both curl and a bare .NET " +
                $"HttpClient at test-authoring time), not a transient outage. Body (first 300 chars): " +
                $"{body[..Math.Min(300, body.Length)]}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("data", out var dataEl),
                $"104 response has no top-level 'data' field — Job104Provider reads exactly this. Body: {body[..Math.Min(500, body.Length)]}");

            // Job104Provider supports two shapes: v2 has data as a bare array; legacy has
            // data.list[]. Whichever one is live right now is worth knowing either way.
            JsonElement listEl;
            if (dataEl.ValueKind == JsonValueKind.Array)
            {
                listEl = dataEl;
            }
            else
            {
                Assert.True(dataEl.TryGetProperty("list", out listEl),
                    "104's 'data' field is neither a bare array (current v2 shape) nor an object with a " +
                    "'list' property (legacy shape) — this is a genuinely new response shape Job104Provider " +
                    $"doesn't handle at all. Body: {body[..Math.Min(500, body.Length)]}");
                Assert.Fail(
                    "104's response used the LEGACY 'data.list[]' shape instead of the current v2 bare-array " +
                    "shape. Job104Provider's fallback handles this, so parsing itself isn't broken, but this is " +
                    "worth flagging: it suggests 104 reverted their API format, which is unexpected and worth a " +
                    "manual look rather than silently continuing.");
            }

            Assert.True(listEl.ValueKind == JsonValueKind.Array && listEl.GetArrayLength() > 0,
                $"104 search for '{SearchKeyword}' returned zero job listings — either the API's shape changed " +
                "again or this keyword genuinely has no current postings (unlikely for '工程師').");

            var first = listEl[0];
            foreach (var requiredField in new[] { "jobNo", "jobName", "custName" })
            {
                Assert.True(first.TryGetProperty(requiredField, out var value) &&
                    value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()),
                    $"104's first job result is missing a non-empty '{requiredField}' — Job104Provider.ParseJob " +
                    "requires this field and will throw (caught per-item, but the job silently disappears) if it's gone.");
            }
        }

        // ── C2: Cake ─────────────────────────────────────────────────────────────────────────

        private static HttpClient CreateCakeClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // Mirrors the "Cake" named HttpClient registration in ServiceCollectionExtensions.cs.
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.cake.me");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.cake.me/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return client;
        }

        [Fact]
        public async Task CakeJobsSearch_KnownKeyword_ReturnsParsableJobsWithRequiredFields()
        {
            using var client = CreateCakeClient();
            var body = new
            {
                query = SearchKeyword,
                filters = new { locations = new[] { "Taiwan" } },
                sort_by = "latest",
                page = 1,
                per_page = 10,
            };

            // No 403 soft-skip here on purpose: the previous HTML-scrape version of this test
            // soft-skipped 403 as "datacenter IP blocked", which hid the real break — Cake had started
            // 308-redirecting search URLs to Cloudflare-challenged SEO paths, so production got zero
            // jobs too. A 403 from the API host is a genuine signal worth a manual look.
            using var response = await client.PostAsJsonAsync("https://api.cake.me/api/client/v2/jobs/search", body);
            var text = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode,
                $"Cake search API request failed: HTTP {(int)response.StatusCode} {response.StatusCode}. " +
                "A 422 usually means the request body contract changed (e.g. a newly required field); a 403 with a " +
                "'Just a moment...' page means Cloudflare now challenges the API host as well. " +
                $"Body (first 300 chars): {text[..Math.Min(300, text.Length)]}");

            using var doc = JsonDocument.Parse(text);
            Assert.True(doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array,
                "Cake search response has no top-level 'data' array — CakeJobsProvider reads exactly this and will " +
                $"now silently return zero jobs. Body: {text[..Math.Min(500, text.Length)]}");

            Assert.True(data.GetArrayLength() > 0,
                $"Cake search for '{SearchKeyword}' in Taiwan returned zero jobs — either the filter/body contract " +
                "changed so nothing matches, or the shape changed (unlikely for '工程師' to have no postings).");

            var first = data[0];
            Assert.True(first.TryGetProperty("title", out var title) &&
                title.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString()),
                "The first Cake job has no non-empty 'title' — CakeJobsProvider.ParseJob requires this to even consider the entry.");

            // Cake-hosted jobs link via path + page.path; aggregated external ones via source_url.
            var isExternal = first.TryGetProperty("job_source", out var src) && src.GetString() == "external";
            if (isExternal)
            {
                Assert.True(first.TryGetProperty("source_url", out _) && first.TryGetProperty("external_company", out _),
                    "External Cake job lacks 'source_url'/'external_company' — CakeJobsProvider falls back to a generic URL/company.");
            }
            else
            {
                Assert.True(first.TryGetProperty("path", out _) &&
                    first.TryGetProperty("page", out var page) && page.TryGetProperty("path", out _),
                    "Cake-hosted job lacks 'path'/'page.path' — CakeJobsProvider can no longer build the job URL.");
            }
        }
    }
}
