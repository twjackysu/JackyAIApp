using System.Text.Json;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract test for open.er-api.com, the USD/TWD source PortfolioService uses for every
    /// TWD conversion in the summary/history/dividend calculations, and the macro view's exchange
    /// rates. A single fixed endpoint — no per-symbol sampling needed.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class FxRateContractTests : LiveContractBase
    {
        private const string Url = "https://open.er-api.com/v6/latest/USD";

        [Fact]
        public async Task UsdTwdRate_HasUsableValue()
        {
            using var response = await Http.GetAsync(Url);
            Assert.True(response.IsSuccessStatusCode, $"open.er-api.com returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            Assert.True(doc.RootElement.TryGetProperty("rates", out var rates),
                $"open.er-api.com response missing 'rates' — PortfolioService.FetchUsdTwdRateAsync reads this exact path. Body: {json}");
            Assert.True(rates.TryGetProperty("TWD", out var twd) && twd.ValueKind == JsonValueKind.Number,
                "open.er-api.com 'rates.TWD' missing or not numeric.");

            var rate = twd.GetDecimal();
            // Sanity band, not a specific value — USD/TWD has stayed roughly in this range for
            // years; a wildly different number (e.g. 0.03 from a units/format change, or a rate
            // for the wrong currency) should fail loudly rather than silently corrupt every
            // TWD-converted number in the app.
            Assert.True(rate is > 15 and < 60,
                $"USD/TWD rate {rate} is outside the sane 15–60 band — possible unit/format change upstream.");

            // TWSEMacroEconomyProvider quotes these against TWD through the same answer.
            foreach (var currency in new[] { "JPY", "EUR", "CNY", "GBP" })
                Assert.True(rates.TryGetProperty(currency, out var perUsd) && perUsd.ValueKind == JsonValueKind.Number && perUsd.GetDecimal() > 0,
                    $"open.er-api.com 'rates.{currency}' missing or not a positive number.");
        }
    }
}
