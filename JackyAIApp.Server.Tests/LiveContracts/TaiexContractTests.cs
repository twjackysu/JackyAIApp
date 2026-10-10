using System.Text.Json;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.LiveContracts
{
    /// <summary>
    /// Live contract tests for what the 回落煞車 signal (Issue #431) is built on:
    /// TWSE <c>rwd/zh/TAIEX/MFI94U</c> (發行量加權股價報酬指數, one month per request) and
    /// OpenAPI <c>holidaySchedule</c> (which days the market was shut).
    ///
    /// Both parsers locate their data by header / field text, so a changed layout would not crash
    /// production: it would stop the refresh, or quietly mis-flag a day. These tests are where that
    /// is noticed. They also print the real field names (look for "[live-contract]" in the run
    /// output), which is the record of what the layout actually is.
    ///
    /// MFI94U is requested exactly once per run: the Boss's rule for www.twse.com.tw is a gap of at
    /// least 2 seconds between requests, and nothing here may loosen it.
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
            var json = await TryFetchStringAsync(TwseHolidayCalendar.Url, "TWSE holidaySchedule");
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
                Assert.True(TwseHolidayCalendar.TryParseDate(dateText.GetString()!, out var date),
                    $"holidaySchedule \"Date\" {dateText} is in a format TwseHolidayCalendar cannot read.");
                dates.Add(date);
            }

            var thisYear = DateTime.UtcNow.AddHours(8).Year;
            Assert.True(dates.Any(d => d.Year >= thisYear - 1),
                $"holidaySchedule has no date from {thisYear - 1} on (latest: {dates.Max()}).");

            // And the production reader gets an answer out of the same live data.
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("ExternalReadOnly")).Returns(() => Http);
            var calendar = new TwseHolidayCalendar(factory.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwseHolidayCalendar>.Instance);
            var sample = dates.Max();
            Assert.NotNull(await calendar.IsMarketClosedAsync(sample));
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
