using System.Text.Json;
using JackyAIApp.Server.Services.Market.US;
using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for Yahoo Finance's unofficial v8 chart API (query1.finance.yahoo.com) —
    /// consumed directly by YahooFinanceMarketDataProvider, DividendService, MarketOverviewProvider,
    /// MarketIndexHistoryProvider (^TWII 6y daily), and PortfolioService's US pricing path
    /// (THIRD_PARTY_CONTRACT_TESTS_PLAN.md section A1).
    /// Not official/documented — no SLA, so this is the highest-value contract to watch.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class YahooChartContractTests : LiveContractBase
    {
        private const string ChartUrl = "https://query1.finance.yahoo.com/v8/finance/chart";

        private static async Task<JsonDocument> FetchChartAsync(string symbol, string query)
        {
            var url = $"{ChartUrl}/{Uri.EscapeDataString(symbol)}?{query}";
            using var response = await Http.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"Yahoo chart request for '{symbol}' failed: HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var result = doc.RootElement.GetProperty("chart").GetProperty("result");
            Assert.True(result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0,
                $"Yahoo chart response for '{symbol}' has no chart.result[] — shape changed or symbol delisted. Body: {json}");

            return doc;
        }

        [Theory]
        [MemberData(nameof(TodaysPicks))]
        public async Task PriceHistory_ForTodaysRandomPicks_HasUsablePriceData(string symbol)
        {
            using var doc = await FetchChartAsync(symbol, "range=5d&interval=1d");
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];

            var meta = result.GetProperty("meta");
            Assert.True(meta.TryGetProperty("currency", out var currency) && !string.IsNullOrEmpty(currency.GetString()),
                $"[{symbol}] meta.currency missing — YahooFinanceMarketDataProvider/DividendService read this for FX conversion.");

            Assert.True(result.TryGetProperty("timestamp", out var timestamps) &&
                timestamps.ValueKind == JsonValueKind.Array && timestamps.GetArrayLength() > 0,
                $"[{symbol}] chart.result[0].timestamp missing/empty — providers index price bars by this array.");

            var quote = result.GetProperty("indicators").GetProperty("quote")[0];
            Assert.True(quote.TryGetProperty("close", out var closes),
                $"[{symbol}] indicators.quote[0].close missing — this is the field every price provider reads.");
            Assert.True(closes.ValueKind == JsonValueKind.Array,
                $"[{symbol}] indicators.quote[0].close is not an array.");

            var hasUsableClose = closes.EnumerateArray()
                .Any(c => c.ValueKind == JsonValueKind.Number && c.GetDecimal() > 0);
            Assert.True(hasUsableClose, $"[{symbol}] every close price in the range was null/zero.");
        }

        public static IEnumerable<object[]> TodaysPicks()
        {
            // TW symbols need the .TW suffix for this endpoint (see YahooFinanceMarketDataProvider/DividendService).
            foreach (var s in StockPoolProvider.PickForToday(StockPoolProvider.TwStocks, count: 3, seedOffset: 1))
                yield return new object[] { $"{s}.TW" };
            foreach (var s in StockPoolProvider.PickForToday(StockPoolProvider.UsStocks, count: 3, seedOffset: 2))
                yield return new object[] { s };
        }

        [Theory]
        [InlineData("0050.TW")] // long-running TW ETF, distributes consistently
        [InlineData("AAPL")]    // long-running US dividend payer
        public async Task DividendsEvents_ForKnownDividendPayers_ParseCorrectly(string symbol)
        {
            // Fixed (not randomly sampled) symbols — this test needs a guaranteed positive case to
            // actually exercise the events.dividends parsing path; a randomly-picked growth stock
            // with zero dividends would let a real schema break pass silently.
            using var doc = await FetchChartAsync(symbol, "range=5y&interval=1mo&events=div");
            var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];

            Assert.True(result.TryGetProperty("events", out var events),
                $"[{symbol}] chart.result[0].events missing entirely.");
            Assert.True(events.TryGetProperty("dividends", out var dividends),
                $"[{symbol}] events.dividends missing — DividendService reads exactly this path.");
            Assert.True(dividends.ValueKind == JsonValueKind.Object && dividends.EnumerateObject().Any(),
                $"[{symbol}] events.dividends is empty over a 5y window — either Yahoo changed the shape, or this " +
                $"symbol genuinely stopped paying (check manually before assuming a schema break).");

            var first = dividends.EnumerateObject().First().Value;
            Assert.True(first.TryGetProperty("amount", out var amount) &&
                amount.ValueKind == JsonValueKind.Number && amount.GetDecimal() > 0,
                $"[{symbol}] a dividend entry's 'amount' field is missing/non-numeric/zero.");
            Assert.True(first.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.Number,
                $"[{symbol}] a dividend entry's 'date' (unix seconds) field is missing/non-numeric.");

            // Sanity: the unix timestamp should parse to a real date within the requested window,
            // not e.g. 0 or a far-future/garbage value from a format change.
            var exDate = DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime;
            Assert.True(exDate > DateTime.UtcNow.AddYears(-6) && exDate < DateTime.UtcNow.AddDays(1),
                $"[{symbol}] dividend date {exDate:yyyy-MM-dd} is outside the expected 5-year window — 'date' unit may have changed.");
        }

        [Fact]
        public async Task TaiexSixYearDailyHistory_PassesMarketIndexHistoryProviderChecks()
        {
            // The exact request MarketIndexHistoryProvider sends (range=6y, interval=1d), run through
            // the exact parser and sanity checks production uses: >= 1,000 points, newest within 10
            // days, no single-day move over 15%. Shape only — never a specific index level.
            using var response = await Http.GetAsync(MarketIndexHistoryProvider.Url);
            Assert.True(response.IsSuccessStatusCode,
                $"Yahoo ^TWII 6y chart request failed: HTTP {(int)response.StatusCode}");
            var json = await response.Content.ReadAsStringAsync();

            var (history, rejection) = MarketIndexHistoryProvider.Parse(json, DateTimeOffset.UtcNow);

            Assert.True(history is not null,
                $"MarketIndexHistoryProvider would reject Yahoo's ^TWII 6y reply ({rejection}) — " +
                "/api/finance/market-index-history would answer 'unavailable'. Check the shape, then the data.");
            Assert.True(history.Points.Zip(history.Points.Skip(1), (a, b) => a.Date < b.Date).All(ok => ok),
                "dates are not strictly ascending");
            Assert.All(history.Points, p => Assert.True(p.Close > 0, $"{p.Date} close is not positive"));
            Assert.Equal(history.Points[^1].Date, history.AsOf);
        }

        [Theory]
        [InlineData("^TWII")] // TAIEX index — no "currency"-denominated price the same way an equity does
        [InlineData("TWD=X")] // FX pair — MarketOverviewProvider/PortfolioService's FX-adjacent quotes
        public async Task IndexAndFxQuotes_HaveUsableMetaFields(string symbol)
        {
            using var doc = await FetchChartAsync(symbol, "range=1d&interval=1d");
            var meta = doc.RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");

            var hasPrice = (meta.TryGetProperty("regularMarketPrice", out var p) && p.ValueKind == JsonValueKind.Number) ||
                           (meta.TryGetProperty("chartPreviousClose", out var pc) && pc.ValueKind == JsonValueKind.Number);
            Assert.True(hasPrice,
                $"[{symbol}] neither meta.regularMarketPrice nor meta.chartPreviousClose present — " +
                $"MarketOverviewProvider.GetDecimal() reads exactly these two fields.");
        }
    }
}
