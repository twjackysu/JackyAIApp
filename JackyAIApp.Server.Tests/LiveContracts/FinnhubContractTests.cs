using System.Text.Json;
using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract test for Finnhub's general news endpoint (B9, consumed by
    /// FinnhubNewsProvider). THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B9.
    ///
    /// Requires FINNHUB_API_KEY — soft-skips (trivially passes) when not configured in this
    /// environment, per LiveContractBase.GetApiKey's documented convention.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class FinnhubContractTests : LiveContractBase
    {
        private const string BaseUrl = "https://finnhub.io/api/v1/news";

        [Fact]
        public async Task GeneralNews_HasUsableHeadlineAndRecentDatetime()
        {
            var apiKey = GetApiKey("FINNHUB_API_KEY");
            if (apiKey == null) return; // no key configured in this environment — soft skip

            using var response = await Http.GetAsync($"{BaseUrl}?category=general&token={apiKey}");
            Assert.True(response.IsSuccessStatusCode, $"[Finnhub news] request failed: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0,
                "[Finnhub news] response is not a non-empty JSON array — FinnhubNewsProvider.FetchAsync expects a plain array.");

            var first = doc.RootElement[0];
            Assert.True(first.TryGetProperty("headline", out var headline) && !string.IsNullOrWhiteSpace(headline.GetString()),
                "[Finnhub news] first item's 'headline' missing/empty.");
            Assert.True(first.TryGetProperty("summary", out _), "[Finnhub news] 'summary' field missing.");
            Assert.True(first.TryGetProperty("source", out _), "[Finnhub news] 'source' field missing.");

            Assert.True(first.TryGetProperty("datetime", out var dt) && dt.TryGetInt64(out var seconds) && seconds > 0,
                "[Finnhub news] 'datetime' missing or not a positive unix timestamp.");

            var when = DateTimeOffset.FromUnixTimeSeconds(dt.GetInt64());
            var now = DateTimeOffset.UtcNow;
            Assert.True(when > now.AddYears(-2) && when < now.AddDays(1),
                $"[Finnhub news] 'datetime' {when:O} is outside a plausible recent range (not far past, not future) — got {dt.GetInt64()}.");
        }
    }
}
