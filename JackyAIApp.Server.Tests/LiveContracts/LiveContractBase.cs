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
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // Several of these APIs (Yahoo, BOT) reject requests with no/odd User-Agent.
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            return client;
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
