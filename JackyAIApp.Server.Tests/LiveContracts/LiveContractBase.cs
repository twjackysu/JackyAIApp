namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Every live contract test class must carry <c>[Trait("Category", "LiveContract")]</c>
    /// (see any class in this folder for the exact usage). It hits the real third-party API to
    /// verify the response shape our parsers depend on hasn't changed
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section 4). This trait is what CI.yml's
    /// <c>--filter Category!=LiveContract</c> excludes from PR builds — a third party being flaky
    /// or down must never turn an unrelated PR red. Only the scheduled contract-tests workflow
    /// runs these (<c>--filter Category=LiveContract</c>).
    ///
    /// These tests assert *structure*, not *values*: that expected fields exist, parse to the
    /// right type, and fall in a sane range (price &gt; 0, date recent, percentage 0–100). They
    /// never assert a specific price/value, since that changes constantly and would be a
    /// guaranteed false positive.
    /// </summary>
    /// Shared plumbing for live contract tests: a real (non-mocked) HttpClient with the headers
    /// each provider needs, plus API-key lookup for the sources that require one.
    /// </summary>
    public abstract class LiveContractBase
    {
        protected static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            // Raised 15s -> 30s -> 100s. TWSE's bulk opendata dumps are big and getting bigger:
            // t187ap11_L (董監事持股, every listed company's director-holdings disclosures) measured
            // ~9s/curl when the 30s ceiling was set, and 10.4MB/21.9s from Taiwan as of 2026-08-18.
            // A GitHub-hosted runner in Azure East US is on a materially worse path to a Taiwanese
            // government host than a local Taiwanese connection, so ~22s locally leaves no useful
            // margin at 30s — hence 7 of the last 8 scheduled runs failing on pure timeouts.
            // 100s is deliberately generous: this is a nightly job, so failing fast buys nothing,
            // while a false failure costs the entire signal (see TryFetchStringAsync).
            // Overridable via LIVE_CONTRACT_TIMEOUT_SECONDS so the scheduled workflow can be tuned
            // without a code change if TWSE's payloads keep growing — and so the skip-on-timeout
            // path below can be exercised deliberately (set it to 1 and every bulk endpoint skips).
            var timeoutSeconds =
                int.TryParse(Environment.GetEnvironmentVariable("LIVE_CONTRACT_TIMEOUT_SECONDS"), out var configured)
                && configured > 0
                    ? configured
                    : 100;
            // gzip/brotli: the runner sits in a US datacenter, where Taiwanese hosts can be slow
            // enough (TPEx measured a few KB a second from East US) that the bytes on the wire
            // decide whether a bulk dump arrives at all.
            var client = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            })
            { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            // Several of these APIs (Yahoo, BOT) reject requests with no/odd User-Agent.
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            return client;
        }

        /// <summary>
        /// GETs a URL, returning null if the request times out rather than letting the exception
        /// fail the test. These tests exist to answer one question — "did the response *shape*
        /// change so our parsers would break?" — and a timeout answers it with "don't know", not
        /// "yes". Reporting inconclusive as failure is actively harmful: 7 of the 8 scheduled runs
        /// before 2026-08-18 were red purely from timeouts on TWSE's bulk endpoints, which trains
        /// everyone to ignore the workflow and buries any genuine contract break in the noise.
        ///
        /// Deliberately narrow — ONLY timeouts are swallowed. A non-success HTTP status, a changed
        /// payload shape, or a missing field all still fail loudly, so a genuinely broken or
        /// removed endpoint is still caught. Follows the same "return null, caller returns early"
        /// convention as <see cref="GetApiKey"/>, since xunit v2 has no dynamic skip.
        /// </summary>
        protected static async Task<string?> TryFetchStringAsync(string url, string label)
        {
            try
            {
                using var response = await Http.GetAsync(url);
                Assert.True(response.IsSuccessStatusCode,
                    $"{label} request failed: HTTP {(int)response.StatusCode}");
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
            {
                // Not Assert.Fail — see the summary above. Surfaced in the test output so a
                // permanently-unreachable endpoint is still visible to anyone reading the log.
                Console.WriteLine(
                    $"[live-contract] SKIPPED {label}: timed out after {Http.Timeout.TotalSeconds:F0}s. " +
                    "Treated as inconclusive, not a contract break.");
                return null;
            }
        }

        /// <summary>
        /// Reads an API key from the environment (set via GitHub Secrets on the scheduled
        /// workflow). Returns null when absent so the caller can skip gracefully — xunit v2 (this
        /// project's version) has no first-class dynamic skip, so the convention here is: if this
        /// returns null, the test body should just `return` early. A trivially-passing test is the
        /// right behavior for "key not configured in this environment", not a failure.
        /// </summary>
        protected static string? GetApiKey(string environmentVariableName)
        {
            var key = Environment.GetEnvironmentVariable(environmentVariableName);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
    }
}
