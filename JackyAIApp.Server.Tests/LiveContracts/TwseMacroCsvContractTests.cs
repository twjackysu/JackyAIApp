using System.Globalization;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract test for the CSV endpoint TWSEMacroEconomyProvider fetches alongside TWSE
    /// OpenAPI: CBC bank interest rates (B6). Its exchange rates come from open.er-api.com, covered
    /// by <see cref="FxRateContractTests"/>.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class TwseMacroCsvContractTests : LiveContractBase
    {
        private const string CbcRateUrl = "https://www.cbc.gov.tw/public/data/OpenData/A13Rate.csv";

        // === B6: CBC bank interest rate CSV ===

        [Fact]
        public async Task CbcRates_HasUsableColumnsForKnownBanks()
        {
            using var response = await Http.GetAsync(CbcRateUrl);
            Assert.True(response.IsSuccessStatusCode, $"[CBC A13Rate] request failed: HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length > 1, "[CBC A13Rate] fewer than 2 lines — can't tell header from data rows.");

            var knownBanks = new[] { "臺灣銀行", "第一銀行" };
            var matched = false;
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length < 31) continue; // TWSEMacroEconomyProvider.FetchBankRates reads up to cols[30]

                var bank = cols[0].Trim();
                if (!knownBanks.Contains(bank)) continue;

                matched = true;
                Assert.True(decimal.TryParse(cols[14], NumberStyles.Any, CultureInfo.InvariantCulture, out _) || string.IsNullOrWhiteSpace(cols[14]),
                    $"[CBC A13Rate/{bank}] cols[14] (定存一年期固定) is not empty/blank and not a parseable number — got '{cols[14]}'.");
                Assert.True(decimal.TryParse(cols[15], NumberStyles.Any, CultureInfo.InvariantCulture, out _) || string.IsNullOrWhiteSpace(cols[15]),
                    $"[CBC A13Rate/{bank}] cols[15] (定存一年期機動) is not empty/blank and not a parseable number — got '{cols[15]}'.");
                Assert.True(decimal.TryParse(cols[30], NumberStyles.Any, CultureInfo.InvariantCulture, out _) || string.IsNullOrWhiteSpace(cols[30]),
                    $"[CBC A13Rate/{bank}] cols[30] (基準利率) is not empty/blank and not a parseable number — got '{cols[30]}'.");
            }

            Assert.True(matched,
                $"[CBC A13Rate] No row matched {string.Join("/", knownBanks)} in column 0 with >=31 columns — " +
                "TWSEMacroEconomyProvider.FetchBankRates filters on this exact set of bank names.");
        }
    }
}
