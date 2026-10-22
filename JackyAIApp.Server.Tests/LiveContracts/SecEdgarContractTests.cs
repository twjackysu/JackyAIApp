using System.Text.Json;
using JackyAIApp.Server.Services.Market.US;
using HtmlAgilityPack;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for SEC EDGAR — company_tickers.json + XBRL companyfacts (B7, consumed
    /// by SECFundamentalDataProvider) and the own-disp HTML ownership table (B8, consumed by
    /// SECInsiderTradingProvider). THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B7/B8.
    ///
    /// SEC requires a contact-info User-Agent or it 403s; rate limit is 10 req/s, so this class
    /// deliberately keeps requests sequential and small (a couple of symbols, not the full pool).
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class SecEdgarContractTests : LiveContractBase
    {
        private const string TickersUrl = "https://www.sec.gov/files/company_tickers.json";
        private const string FactsUrlTemplate = "https://data.sec.gov/api/xbrl/companyfacts/CIK{0}.json";
        private const string OwnershipUrlTemplate = "https://www.sec.gov/cgi-bin/own-disp?action=getissuer&CIK={0}&type=&dateb=&owner=include&start=0";
        private const string UserAgent = "JackyAIApp/1.0 jacky19918@gmail.com";

        private static HttpRequestMessage BuildRequest(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            // .NET's strict header parser rejects '@' in User-Agent without this — same pattern as
            // SECFundamentalDataProvider.CreateClient/SECInsiderTradingProvider.CreateClient.
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            return request;
        }

        // === B7: ticker→CIK lookup + XBRL company facts ===

        [Fact]
        public async Task CompanyTickers_ResolvesAaplToA10DigitCik()
        {
            using var response = await Http.SendAsync(BuildRequest(TickersUrl));
            Assert.True(response.IsSuccessStatusCode, $"[company_tickers.json] request failed: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            string? resolvedCik = null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("ticker", out var t) && t.GetString() == "AAPL")
                {
                    resolvedCik = prop.Value.GetProperty("cik_str").GetInt64().ToString("D10");
                    break;
                }
            }

            Assert.True(resolvedCik != null, "[company_tickers.json] no entry with ticker=='AAPL' — SECFundamentalDataProvider.ResolveCIK filters on this exact field.");
            Assert.Equal(10, resolvedCik!.Length);
            Assert.True(long.TryParse(resolvedCik, out _), $"[company_tickers.json] resolved CIK '{resolvedCik}' is not all-digits.");
        }

        [Fact]
        public async Task CompanyFacts_ForAapl_HasUsGaapFactsWithEpsAndRevenue()
        {
            var cik = "0000320193"; // AAPL — verified against company_tickers.json in the test above during implementation
            var url = string.Format(FactsUrlTemplate, cik);
            using var response = await Http.SendAsync(BuildRequest(url));
            Assert.True(response.IsSuccessStatusCode, $"[companyfacts/AAPL] request failed: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("facts", out var facts), "[companyfacts/AAPL] 'facts' object missing.");
            Assert.True(facts.TryGetProperty("us-gaap", out var usgaap), "[companyfacts/AAPL] 'facts.us-gaap' object missing — SECFundamentalDataProvider.ExtractFundamentals reads this exact path.");

            Assert.True(usgaap.TryGetProperty("EarningsPerShareDiluted", out var eps) &&
                eps.TryGetProperty("units", out var epsUnits) && epsUnits.TryGetProperty("USD/shares", out var epsEntries) &&
                epsEntries.GetArrayLength() > 0 && epsEntries[0].TryGetProperty("val", out _),
                "[companyfacts/AAPL] 'EarningsPerShareDiluted.units.USD/shares[]' missing or empty — SECFundamentalDataProvider's primary EPS concept.");

            // The provider tries RevenueFromContractWithCustomerExcludingAssessedTax first, then
            // falls back to Revenues. Note which one(s) survive — both disappearing is the real break.
            var hasPrimaryRevenue = usgaap.TryGetProperty("RevenueFromContractWithCustomerExcludingAssessedTax", out var primaryRev) &&
                primaryRev.TryGetProperty("units", out var primaryUnits) && primaryUnits.TryGetProperty("USD", out var primaryEntries) &&
                primaryEntries.GetArrayLength() > 0;
            var hasFallbackRevenue = usgaap.TryGetProperty("Revenues", out var fallbackRev) &&
                fallbackRev.TryGetProperty("units", out var fallbackUnits) && fallbackUnits.TryGetProperty("USD", out var fallbackEntries) &&
                fallbackEntries.GetArrayLength() > 0;

            Assert.True(hasPrimaryRevenue || hasFallbackRevenue,
                "[companyfacts/AAPL] Neither 'RevenueFromContractWithCustomerExcludingAssessedTax' nor the 'Revenues' " +
                "fallback concept has usable USD entries — SECFundamentalDataProvider.ComputeQuarterlyRevenueWithYoY " +
                "would return null for both, a real contract break." +
                (hasPrimaryRevenue ? "" : " (primary concept is gone; only the fallback still works — worth noting even though not a hard failure.)"));
        }

        // === B8: insider trading HTML table (most fragile integration in the plan) ===

        [Fact]
        public async Task InsiderOwnership_ForAapl_HasTransactionReportTableWithParseableTransactionCodes()
        {
            var cik = "0000320193"; // AAPL
            var url = string.Format(OwnershipUrlTemplate, cik);
            using var response = await Http.SendAsync(BuildRequest(url));
            Assert.True(response.IsSuccessStatusCode, $"[own-disp/AAPL] request failed: HTTP {(int)response.StatusCode}");

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//table[@id='transaction-report']");
            Assert.True(table != null,
                "[own-disp/AAPL] no <table id='transaction-report'> found — SECInsiderTradingProvider.ParseOwnershipTable's " +
                "root selector. This is the most fragile integration in the plan (raw HTML scrape); if this ever fires, " +
                "the page structure has genuinely changed, not just gone quiet.");

            var rows = table!.SelectNodes(".//tr[position()>1]"); // skip header row, same as production code
            if (rows == null || rows.Count == 0)
            {
                // Table structure survived but has zero data rows. Insider trading volume is
                // legitimately variable (a company can simply have no recent Form 4 filings in
                // whatever window SEC returns) — soft-tolerate per plan section 4.2/7, don't hard-fail.
                Assert.True(true, "[own-disp/AAPL] 'transaction-report' table exists but has zero data rows — " +
                    "table structure is intact, this is plausible (no recent filings), not asserted as a failure.");
                return;
            }

            var anyCodeExtracted = false;
            foreach (var row in rows)
            {
                var cells = row.SelectNodes(".//td")?.Select(c => c.InnerText.Trim()).ToArray();
                if (cells == null || cells.Length < 9) continue;

                var txTypeRaw = cells.ElementAtOrDefault(5) ?? "";
                var code = ExtractTransactionCode(txTypeRaw);
                if (!string.IsNullOrEmpty(code))
                {
                    anyCodeExtracted = true;
                    break;
                }
            }

            Assert.True(anyCodeExtracted,
                "[own-disp/AAPL] 'transaction-report' has data rows with >=9 cells, but none of them yielded a " +
                "valid P/S/M/A/D transaction code from column 5 — SECInsiderTradingProvider.ExtractTransactionCode " +
                "would silently drop every row. Real contract break, not the benign 'no recent filings' case.");
        }

        /// <summary>Mirrors SECInsiderTradingProvider.ExtractTransactionCode exactly (private in production code).</summary>
        private static string ExtractTransactionCode(string typeString)
        {
            if (string.IsNullOrWhiteSpace(typeString)) return "";
            var parts = typeString.Split('-', ' ');
            var code = parts[0].Trim().ToUpperInvariant();
            return code.Length == 1 && "PSMAD".Contains(code) ? code : "";
        }
    }
}
