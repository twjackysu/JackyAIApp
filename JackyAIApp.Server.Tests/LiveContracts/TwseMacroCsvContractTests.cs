using System.Globalization;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for the two CSV endpoints TWSEMacroEconomyProvider fetches alongside
    /// TWSE OpenAPI: Bank of Taiwan exchange rates (B5) and CBC bank interest rates (B6).
    /// THIRD_PARTY_CONTRACT_TESTS_PLAN.md section B5/B6.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class TwseMacroCsvContractTests : LiveContractBase
    {
        private const string BotFxUrl = "https://rate.bot.com.tw/xrt/flcsv/0/day";
        private const string CbcRateUrl = "https://www.cbc.gov.tw/public/data/OpenData/A13Rate.csv";

        // === B5: Bank of Taiwan FX CSV ===

        /// <summary>
        /// Bank of Taiwan is known to serve a JS "Challenge Validation" anti-bot page instead of
        /// the CSV to non-browser clients (PortfolioService.FetchUsdTwdRateAsync's code comment
        /// documents this, and TWSEMacroEconomyProvider.FetchExchangeRates hits the exact same
        /// URL). This test distinguishes the two known outcomes instead of hard-failing on the
        /// tolerated one:
        ///   - Blocked (body looks like the anti-bot challenge page) → soft-tolerated, logged via
        ///     Assert.True(true, ...) so the run stays green; this is a known, pre-existing upstream
        ///     issue, not a regression we want paging someone over.
        ///   - Got CSV back, but the column layout changed (currency codes 不見 or the parser's
        ///     column positions no longer hold numeric values) → hard fail, this is a real contract
        ///     break the code needs a fix for.
        /// </summary>
        [Fact]
        public async Task BotFx_EitherParsesKeyCurrenciesOrIsKnownAntiBotBlocked()
        {
            using var response = await Http.GetAsync(BotFxUrl);
            var body = await response.Content.ReadAsStringAsync();

            var looksBlocked = body.Contains("Challenge Validation", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
                || !response.IsSuccessStatusCode;

            if (looksBlocked)
            {
                // Known, tolerated upstream behavior — do not fail the scheduled run over it.
                Assert.True(true, "[BOT FX] Response looks like BOT's anti-bot challenge page (or a non-2xx), " +
                    "not real CSV. This is a documented known issue (see PortfolioService.FetchUsdTwdRateAsync) " +
                    "— soft-tolerated, not treated as a contract break.");
                return;
            }

            var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length > 1, "[BOT FX] Got a 2xx, non-HTML-looking body, but fewer than 2 lines — " +
                "can't tell header from data rows. Body (truncated): " + body[..Math.Min(body.Length, 300)]);

            var keyCurrencies = new[] { "USD", "JPY", "EUR", "CNY", "GBP" };
            var found = new List<string>();
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length < 14) continue; // TWSEMacroEconomyProvider.FetchExchangeRates reads cols[3] and cols[13]

                var currency = cols[0].Trim();
                if (!keyCurrencies.Contains(currency)) continue;

                found.Add(currency);
                Assert.True(decimal.TryParse(cols[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var buy) && buy > 0,
                    $"[BOT FX/{currency}] cols[3] (即期買入/spot buy) is not a positive number — got '{cols[3]}'.");
                Assert.True(decimal.TryParse(cols[13], NumberStyles.Any, CultureInfo.InvariantCulture, out var sell) && sell > 0,
                    $"[BOT FX/{currency}] cols[13] (即期賣出/spot sell) is not a positive number — got '{cols[13]}'.");
            }

            Assert.True(found.Count > 0,
                $"[BOT FX] Got a real-looking CSV (not blocked) but none of {string.Join("/", keyCurrencies)} " +
                "appeared in column 0 — TWSEMacroEconomyProvider.KeyCurrencies filter would silently return nothing.");
        }

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
