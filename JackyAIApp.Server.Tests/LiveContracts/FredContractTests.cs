using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for FRED's series/observations endpoint (B10, consumed by
    /// FredIndicatorProvider). THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B10.
    ///
    /// Requires FRED_API_KEY — soft-skips (trivially passes) when not configured in this
    /// environment, per LiveContractBase.GetApiKey's documented convention. Covers 3 of the 6
    /// series FredIndicatorProvider.Indicators actually uses (each is an independent FRED series
    /// that could drift separately) rather than all 6, to keep the request count modest.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class FredContractTests : LiveContractBase
    {
        private const string BaseUrl = "https://api.stlouisfed.org/fred/series/observations";

        public static IEnumerable<object[]> SampledSeries() =>
            new[] { "DGS10", "VIXCLS", "M2SL" }.Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(SampledSeries))]
        public async Task Observations_ForSampledSeries_HasAtLeastOneParseableValue(string seriesId)
        {
            var apiKey = GetApiKey("FRED_API_KEY");
            if (apiKey == null) return; // no key configured in this environment — soft skip

            var start = DateTime.UtcNow.AddMonths(-3).ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}?series_id={seriesId}&api_key={apiKey}&file_type=json&observation_start={start}&sort_order=asc";

            using var response = await Http.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode, $"[FRED/{seriesId}] request failed: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("observations", out var obs) && obs.ValueKind == JsonValueKind.Array,
                $"[FRED/{seriesId}] 'observations' array missing — FredIndicatorProvider.FetchSeriesSafeAsync reads this exact path.");
            Assert.True(obs.GetArrayLength() > 0, $"[FRED/{seriesId}] 'observations' array is empty.");

            var hasParseableValue = false;
            foreach (var o in obs.EnumerateArray())
            {
                Assert.True(o.TryGetProperty("date", out _), $"[FRED/{seriesId}] an observation is missing 'date'.");
                Assert.True(o.TryGetProperty("value", out var v), $"[FRED/{seriesId}] an observation is missing 'value'.");

                var valStr = v.GetString();
                // FRED uses "." as its missing-data sentinel — a shape-only check ("field exists")
                // would pass even if every value were "."; the contract we actually depend on is
                // "at least one observation is real, parseable data".
                if (!string.IsNullOrEmpty(valStr) && valStr != "." &&
                    decimal.TryParse(valStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    hasParseableValue = true;
                    break;
                }
            }

            Assert.True(hasParseableValue,
                $"[FRED/{seriesId}] every observation in the last 3 months has value=='.' (FRED's missing-data " +
                "sentinel) or is otherwise unparseable — FredIndicatorProvider would drop this series entirely.");
        }
    }
}
