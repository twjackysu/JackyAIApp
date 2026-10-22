using System.Text.Json;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using JackyAIApp.Server.Services.Portfolio;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for the two dumps TwListedCompanyService builds the stock-search list
    /// from: TWSE STOCK_DAY_ALL (Code/Name) and TPEx tpex_mainboard_daily_close_quotes
    /// (SecuritiesCompanyCode/CompanyName). A renamed field there would not fail loudly in
    /// production — the refresh would just keep failing and search would quietly age.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class TwListedCompanyListContractTests : LiveContractBase
    {
        // Long-listed bellwethers of each kind, so an absence means the dump changed, not that a
        // symbol was delisted: TWSE stock / TWSE ETF, TPEx stock / TPEx ETF.
        public static TheoryData<string, string, string, string[]> Sources => new()
        {
            { TwseOpenApi.DailyQuotesUrl, "Code", "Name", new[] { "2330", "0050" } },
            { TwListedCompanyService.TpexUrl, "SecuritiesCompanyCode", "CompanyName", new[] { "8069", "006201" } },
        };

        [Theory]
        [MemberData(nameof(Sources))]
        public async Task ListedCompanyDump_HasSymbolAndNameFields(string url, string symbolField, string nameField, string[] expected)
        {
            var json = await TryFetchStringAsync(url, url);
            if (json is null) return;

            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0,
                $"{url} did not return a non-empty JSON array. Body (truncated): {json[..Math.Min(json.Length, 300)]}");

            var rows = doc.RootElement.EnumerateArray()
                .Select(r => (
                    Symbol: r.TryGetProperty(symbolField, out var s) ? s.GetString()?.Trim() ?? "" : "",
                    Name: r.TryGetProperty(nameField, out var n) ? n.GetString()?.Trim() ?? "" : ""))
                .Where(r => !TwListedCompanyService.IsWarrant(r.Symbol))
                .ToList();

            foreach (var symbol in expected)
            {
                var row = rows.FirstOrDefault(r => r.Symbol == symbol);
                Assert.True(row.Symbol == symbol, $"{url}: no row with {symbolField}=='{symbol}'.");
                Assert.False(string.IsNullOrEmpty(row.Name), $"{url}: '{symbol}' has an empty {nameField}.");
            }

            // Several hundred listings on each exchange, warrants excluded. Far below that means
            // the dump shrank to something else (a single day's movers, a page of results).
            Assert.True(rows.Count > 500, $"{url}: only {rows.Count} non-warrant rows.");
        }
    }
}
