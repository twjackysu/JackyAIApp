using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for the two job-board integrations behind the /jobs feature
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section C, C1/C2). Both are unofficial scrapes (one
    /// JSON, one HTML data-island) with no SLA — the highest-value thing this class watches for is
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
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return client;
        }

        /// <summary>
        /// Same marker-based extraction CakeJobsProvider.ExtractNextData uses internally
        /// (duplicated here — it's private on the provider and trivial enough that pulling it
        /// through DI/reflection would add more risk of testing our own wiring than the third
        /// party's shape).
        /// </summary>
        private static string? ExtractNextData(string html)
        {
            const string marker = "<script id=\"__NEXT_DATA__\" type=\"application/json\">";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            return end < 0 ? null : html[start..end];
        }

        [Fact]
        public async Task CakeJobsSearch_KnownKeyword_NextDataIslandParsesToNonEmptyJobMap()
        {
            using var client = CreateCakeClient();
            var encoded = Uri.EscapeDataString(SearchKeyword);
            var url = $"https://www.cake.me/jobs?location_list%5B0%5D=Taiwan&page=1&q={encoded}";

            using var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"Cake jobs page request failed: HTTP {(int)response.StatusCode} {response.StatusCode}.");

            var html = await response.Content.ReadAsStringAsync();
            var nextJson = ExtractNextData(html);

            // This is the single most fragile point in the whole C group (see plan's 脆弱度
            // rating): if Next.js's script id/marker ever changes, CakeJobsProvider goes dark
            // silently (returns zero jobs, no exception) in production. Distinguish that failure
            // mode explicitly from "site is down" or "field renamed".
            Assert.False(nextJson is null,
                "Cake's page no longer contains a <script id=\"__NEXT_DATA__\" type=\"application/json\"> tag. " +
                "This means Cake changed its Next.js data-embedding approach (different script id, moved to a " +
                "different rendering strategy, etc.) — CakeJobsProvider.ExtractNextData will now return null on " +
                "every request and CakeJobsProvider will silently produce zero jobs with no error logged beyond " +
                "an early loop break. This is a 'the whole provider is dark' finding, not a minor field drift.");

            using var doc = JsonDocument.Parse(nextJson!);
            var root = doc.RootElement;

            var hasPath =
                root.TryGetProperty("props", out var props) &&
                props.TryGetProperty("pageProps", out var pageProps) &&
                pageProps.TryGetProperty("initialState", out var initialState) &&
                initialState.TryGetProperty("jobSearch", out var jobSearch) &&
                jobSearch.TryGetProperty("entityByPathId", out var jobMap) &&
                jobMap.ValueKind == JsonValueKind.Object;

            Assert.True(hasPath,
                "__NEXT_DATA__ parsed, but props.pageProps.initialState.jobSearch.entityByPathId is missing or " +
                "not an object. Cake restructured its internal Redux/state shape — CakeJobsProvider.TryGetJobMap " +
                "reads exactly this path and will now silently return zero jobs for every search.");

            var entityByPathId = root.GetProperty("props").GetProperty("pageProps").GetProperty("initialState")
                .GetProperty("jobSearch").GetProperty("entityByPathId");

            Assert.True(entityByPathId.EnumerateObject().Any(),
                $"Cake search for '{SearchKeyword}' returned an empty entityByPathId map — either the site has " +
                "zero results for this common keyword (unlikely) or the shape has changed in a way that empties " +
                "the map without breaking the path itself.");

            var firstJob = entityByPathId.EnumerateObject().First().Value;
            Assert.True(firstJob.TryGetProperty("title", out var title) &&
                title.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString()),
                "The first entry in Cake's entityByPathId map has no non-empty 'title' field — " +
                "CakeJobsProvider.ParseJob requires this to even consider the entry.");
        }
    }
}
