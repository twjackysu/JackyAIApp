using System.Text.Json;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TWStockLib;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for what the 回落煞車 signal (Issue #431) is built on:
    /// TWSE <c>rwd/zh/TAIEX/MFI94U</c> (發行量加權股價報酬指數, one month per request),
    /// OpenAPI <c>holidaySchedule</c> (which days the market was shut), and MIS
    /// <c>getStockInfo.jsp?ex_ch=tse_t00.tw</c> (加權指數 now, for the intraday estimate, TASK-038).
    ///
    /// The parsers locate their data by header / field text, so a changed layout would not crash
    /// production: it would stop the refresh, quietly mis-flag a day, or switch the estimate off.
    /// These tests are where that is noticed. They also print the real field names (look for
    /// "[live-contract]" in the run output), which is the record of what the layout actually is.
    ///
    /// MFI94U is requested exactly once per run: the Boss's rule for www.twse.com.tw is a gap of at
    /// least 2 seconds between requests, and nothing here may loosen it. MIS is likewise one request.
    ///
    /// Only runs on the scheduled contract-tests workflow, never in PR CI (see LiveContractBase).
    /// </summary>
    [Trait("Category", "LiveContract")]
    public class TaiexContractTests : LiveContractBase
    {
        [Fact]
        public async Task MFI94U_LastMonth_HasAFullMonthWithLocatableColumnsAndParsableValues()
        {
            // The previous month is complete, so a short answer means the layout changed, not that
            // the month is still going.
            var month = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddMonths(-1);
            var json = await TryFetchStringAsync(
                $"{TaiexTotalReturnClient.BaseUrl}?date={month.Year:0000}{month.Month:00}01&response=json", "TWSE MFI94U");
            if (json is null) return;

            ReportLayout(json);

            var result = TaiexTotalReturnParser.Parse(json, month.Year, month.Month, NullLogger.Instance);

            Assert.True(result.Success,
                "TaiexTotalReturnParser rejected the MFI94U response — the 日期 / 報酬指數 columns could not be located " +
                $"uniquely, or a row was unreadable. Body (truncated): {json[..Math.Min(json.Length, 400)]}");
            // Even Lunar New Year months have well over 10 trading days.
            Assert.True(result.Points.Count >= 10, $"MFI94U returned only {result.Points.Count} days for {month:yyyy-MM}.");
            Assert.All(result.Points, p =>
            {
                Assert.Equal(month.Year, p.Date.Year);
                Assert.Equal(month.Month, p.Date.Month);
                Assert.True(p.Close > 1000m, $"{p.Date}: total-return index {p.Close} is implausibly low.");
            });
            Assert.Equal(result.Points.Select(p => p.Date).OrderBy(d => d), result.Points.Select(p => p.Date));
        }

        [Fact]
        public async Task HolidaySchedule_IsANonEmptyArrayWithParsableDates_ThatTheCalendarCanRead()
        {
            var json = await TryFetchStringAsync(TwseOpenApi.HolidayScheduleUrl, "TWSE holidaySchedule");
            if (json is null) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0,
                $"holidaySchedule did not return a non-empty JSON array. Body (truncated): {json[..Math.Min(json.Length, 300)]}");

            var rows = root.EnumerateArray().ToList();
            Console.WriteLine($"[live-contract] holidaySchedule: {rows.Count} rows; first row = {rows[0]}");
            Console.WriteLine($"[live-contract] holidaySchedule: names = {string.Join(" | ", rows.Select(NameOf).Distinct().Take(40))}");

            var dates = new List<DateOnly>();
            foreach (var row in rows)
            {
                var dateText = row.ValueKind == JsonValueKind.Object
                    ? row.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "Date", StringComparison.OrdinalIgnoreCase)).Value
                    : default;
                Assert.True(dateText.ValueKind == JsonValueKind.String, $"A holidaySchedule row has no string \"Date\": {row}");
                Assert.True(TwseTradingCalendar.TryParseDate(dateText.GetString()!, out var date),
                    $"holidaySchedule \"Date\" {dateText} is in a format TwseTradingCalendar cannot read.");
                dates.Add(date);
            }

            var thisYear = DateTime.UtcNow.AddHours(8).Year;
            Assert.True(dates.Any(d => d.Year >= thisYear - 1),
                $"holidaySchedule has no date from {thisYear - 1} on (latest: {dates.Max()}).");

            // And the production reader gets an answer out of the same live data.
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => Http);
            var calendar = new TwseTradingCalendar(
                new TwseOpenApi(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseOpenApi>.Instance, TimeProvider.System), NullLogger<TwseTradingCalendar>.Instance);
            Assert.True(await calendar.RefreshAsync(), "TwseTradingCalendar could not load the live schedule.");
            var sample = dates.Max();
            Assert.NotNull(calendar.IsMarketClosed(sample));
        }

        [Fact]
        public async Task Mis_TaiexIndex_HasTheFieldsTheEstimateReads()
        {
            // The same query string TWStockLib sends for a quote (the cache-buster is the clock).
            var json = await TryFetchStringAsync(
                $"https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_{MisTaiexClient.IndexSymbol}.tw&json=1&delay=0&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                "TWSE MIS t00");
            if (json is null) return;

            Console.WriteLine($"[live-contract] MIS t00 body (truncated) = {json[..Math.Min(json.Length, 1200)]}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("msgArray", out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0,
                $"MIS returned no msgArray entry for tse_t00.tw — an empty one is what a missing session or a changed query looks like. Body (truncated): {json[..Math.Min(json.Length, 400)]}");
            var row = rows[0];

            string? Field(string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            Console.WriteLine($"[live-contract] MIS t00 fields = [{string.Join(" | ", row.EnumerateObject().Select(p => p.Name))}]");
            Console.WriteLine($"[live-contract] MIS t00 c={Field("c")} ch={Field("ch")} n={Field("n")} d={Field("d")} t={Field("t")} tlong={Field("tlong")} z={Field("z")} y={Field("y")}");

            // Every field TWStockLib reads for the estimate is there, as the string it expects.
            Assert.True(Field("c") == MisTaiexClient.IndexSymbol || Field("ch") == MisTaiexClient.IndexSymbol + ".tw",
                $"msgArray[0] does not identify itself as {MisTaiexClient.IndexSymbol} (c={Field("c")}, ch={Field("ch")}).");
            Assert.NotNull(Field("d"));
            Assert.NotNull(Field("tlong")); // TWStockLib's SyncTime, the only time the estimate reads
            Assert.NotNull(Field("z"));
            Assert.NotNull(Field("y"));

            // The previous close is a price-index level, whatever time of day this runs.
            Assert.True(decimal.TryParse(Field("y"), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var previous) && previous > 1000m,
                $"y (previous close) {Field("y")} is not a plausible TAIEX level.");
            Assert.True(DateOnly.TryParseExact(Field("d"), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var tradeDate),
                $"d (date) {Field("d")} is not yyyyMMdd.");
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
            Assert.InRange(today.DayNumber - tradeDate.DayNumber, 0, 10);

            // z is "-" until the first trade of the day; the scheduled run lands right at the open, so that
            // is not a contract break. Once there is a trade, production's own path must read the whole entry:
            // TWStockLib's bare GET (no custom User-Agent, like the app's default client) and parser, then the
            // client's checks.
            if (Field("z") == "-")
            {
                Console.WriteLine("[live-contract] MIS t00 z is '-' (no trade yet): the TWStockLib check is skipped this run.");
                return;
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTwStock();
            using var provider = services.BuildServiceProvider();
            var client = new MisTaiexClient(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<MisTaiexClient>.Instance);

            var quote = await client.GetAsync();

            Assert.True(quote is not null,
                "MisTaiexClient (TWStockLib) found no usable reading although the raw MIS entry has z — the library could not read the index entry, or the client rejected it (see the warning above).");
            Console.WriteLine($"[live-contract] MIS t00 via TWStockLib: Last={quote!.Last} PreviousClose={quote.PreviousClose} QuotedAt={quote.QuotedAt:O} TradeDate={quote.TradeDate}");
            Assert.Equal(tradeDate, quote.TradeDate);
            Assert.True(quote.Last > 1000m, $"z (last) {quote.Last} is implausibly low for the TAIEX.");
            Assert.Equal(previous, quote.PreviousClose);
        }

        private static string NameOf(JsonElement row) =>
            row.ValueKind == JsonValueKind.Object
                ? row.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "Name", StringComparison.OrdinalIgnoreCase)).Value.ToString()
                : "";

        /// <summary>Prints the real header names and the first row, so the layout is on record.</summary>
        private static void ReportLayout(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var fields = root.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array
                    ? string.Join(" | ", f.EnumerateArray().Select(e => e.ToString()))
                    : "(no fields array)";
                var first = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0
                    ? d[0].ToString()
                    : "(no data rows)";
                var title = root.TryGetProperty("title", out var t) ? t.ToString() : "(no title)";
                Console.WriteLine($"[live-contract] MFI94U title = {title}");
                Console.WriteLine($"[live-contract] MFI94U fields = [{fields}]");
                Console.WriteLine($"[live-contract] MFI94U first row = {first}");
            }
            catch (JsonException)
            {
                Console.WriteLine($"[live-contract] MFI94U response is not JSON: {json[..Math.Min(json.Length, 200)]}");
            }
        }
    }
}
