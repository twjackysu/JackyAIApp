using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for FundRich (基富通) — a reverse-engineered private JSON API with no
    /// public docs, consumed by FundDataService (search + NAV chart) and DividendService
    /// (dividend history). THIRD_PARTY_CONTRACT_TESTS_PLAN.md section A2 — the single most fragile
    /// integration in the app, since there's no contract to begin with, only observed behavior.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class FundRichContractTests : LiveContractBase
    {
        private const string SearchUrl = "https://apis.fundrich.com.tw/DataCenter/FRSIndex/";
        private const string ChartUrl = "https://apis.fundrich.com.tw/FRSDataCenter/GetFundChart";
        private const string DividendUrl = "https://apis.fundrich.com.tw/FRSDataCenter/GetFundDividendHistory";

        // Known-stable funds used as fixed (not randomly sampled) positive cases — FundRich has no
        // curated "pool" concept like stocks do, and we need a guaranteed dividend-paying/actively
        // listed fund to meaningfully exercise the parsing path.
        private const string KnownFundId = "ALI006";          // 安聯台灣科技基金 — NAV history
        private const string KnownDividendFundId = "076009";  // DWS 高股息(年配)— dividend history

        private static HttpRequestMessage BuildRequest(string url, object payload)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            // FundRich rejects requests without a same-origin Referer/Origin (HTTP 406) — same
            // headers FundDataService/DividendService send.
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.fundrich.com.tw");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.fundrich.com.tw/");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return req;
        }

        [Fact]
        public async Task Search_ByKeyword_ReturnsUsableResultList()
        {
            using var req = BuildRequest(SearchUrl, new { Method = "getSearchKw", Data = new { kw = "高股息" } });
            using var response = await Http.SendAsync(req);
            Assert.True(response.IsSuccessStatusCode, $"FundRich search returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("data", out var data), $"FundRich search: 'data' missing. Body: {json}");
            Assert.True(data.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array,
                "FundRich search: 'data.list' missing/not an array — FundDataService.SearchFundsAsync reads this exact path.");
            Assert.True(list.GetArrayLength() > 0, "FundRich search for '高股息' returned zero results — suspicious for such a common keyword.");

            var first = list[0];
            Assert.True(first.TryGetProperty("fundId", out var fundId) && !string.IsNullOrEmpty(fundId.GetString()),
                "FundRich search result missing 'fundId'.");
            Assert.True(first.TryGetProperty("name", out var name) && !string.IsNullOrEmpty(name.GetString()),
                "FundRich search result missing 'name'.");
        }

        [Fact]
        public async Task NavHistory_ForKnownFund_HasUsablePriceHistory()
        {
            using var req = BuildRequest(ChartUrl, new { data = new { fundId = KnownFundId, period = "1Y" } });
            using var response = await Http.SendAsync(req);
            Assert.True(response.IsSuccessStatusCode, $"FundRich GetFundChart returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("data", out var data), $"FundRich chart: 'data' missing. Body: {json}");
            Assert.True(data.TryGetProperty("tableRow", out var tableRow), "FundRich chart: 'data.tableRow' missing.");
            Assert.True(tableRow.TryGetProperty("currency", out var currency) && !string.IsNullOrEmpty(currency.GetString()),
                "FundRich chart: 'tableRow.currency' missing — FundDataService reads this for FX conversion.");
            Assert.True(tableRow.TryGetProperty("priceHistory", out var priceHistory) && priceHistory.ValueKind == JsonValueKind.Array,
                "FundRich chart: 'tableRow.priceHistory' missing/not an array.");
            Assert.True(priceHistory.GetArrayLength() > 0,
                $"FundRich chart for known fund '{KnownFundId}' returned zero price points.");

            var point = priceHistory[0];
            var dateStr = point.TryGetProperty("date", out var d) ? d.GetString() : null;
            var priceStr = point.TryGetProperty("price", out var p) ? p.GetString() : null;
            Assert.True(!string.IsNullOrEmpty(dateStr) &&
                DateTime.TryParseExact(dateStr, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"FundRich chart price point 'date' ('{dateStr}') doesn't parse as yyyy/MM/dd — FundDataService uses this exact format.");
            Assert.True(!string.IsNullOrEmpty(priceStr) &&
                decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) && price > 0,
                $"FundRich chart price point 'price' ('{priceStr}') doesn't parse as a positive decimal.");
        }

        [Fact]
        public async Task DividendHistory_ForKnownDividendFund_ParsesCorrectly()
        {
            using var req = BuildRequest(DividendUrl, new { data = new { fundId = KnownDividendFundId } });
            using var response = await Http.SendAsync(req);
            Assert.True(response.IsSuccessStatusCode, $"FundRich GetFundDividendHistory returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("data", out var data), $"FundRich dividend history: 'data' missing. Body: {json}");
            Assert.True(data.TryGetProperty("tableRow", out var tableRow), "FundRich dividend history: 'data.tableRow' missing.");
            Assert.True(tableRow.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array,
                "FundRich dividend history: 'tableRow.history' missing/not an array — DividendService.GetFundDividendsAsync reads this exact path.");
            Assert.True(history.GetArrayLength() > 0,
                $"FundRich dividend history for known dividend-paying fund '{KnownDividendFundId}' is empty — either the fund " +
                $"changed distribution policy, or the response shape changed (check manually before assuming a schema break).");

            var entry = history[0];
            var exDateStr = entry.TryGetProperty("exDividendDate", out var ed) ? ed.GetString() : null;
            var amountStr = entry.TryGetProperty("perUnitPrice", out var pu) ? pu.GetString() : null;
            Assert.True(!string.IsNullOrEmpty(exDateStr) &&
                DateTime.TryParseExact(exDateStr, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"FundRich dividend entry 'exDividendDate' ('{exDateStr}') doesn't parse as yyyy/MM/dd.");
            Assert.True(!string.IsNullOrEmpty(amountStr) &&
                decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) && amount > 0,
                $"FundRich dividend entry 'perUnitPrice' ('{amountStr}') doesn't parse as a positive decimal.");
        }
    }
}
