using System.Text.Json;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// TWSE's trading calendar: weekends, plus the market holiday schedule (OpenAPI
    /// <c>holidaySchedule</c>). The schedule is loaded off the request path — at startup and then
    /// twice a day, by <see cref="TradingCalendarRefreshService"/> — and held in memory, so asking
    /// never waits on TWSE. A failed load keeps the schedule already held; until one succeeds, a
    /// weekday is unknown (null), and callers fall back to the weekday rule.
    ///
    /// The response layout was not verifiable when this was written (the build environment cannot
    /// reach twse.com.tw), so reading it is deliberately forgiving and failure-tolerant: anything
    /// unreadable leaves the weekdays unknown. <c>TaiexContractTests</c> checks the layout against
    /// the live API. Entries the schedule lists that are trading days (the first day back after a
    /// break, the last one before it) are not holidays.
    /// </summary>
    public class TwseTradingCalendar(
        IHttpClientFactory httpClientFactory,
        ILogger<TwseTradingCalendar> logger) : ITradingCalendar
    {
        internal const string Url = "https://openapi.twse.com.tw/v1/holidaySchedule/holidaySchedule";

        private sealed record Schedule(HashSet<DateOnly> ClosedDays, HashSet<int> Years);

        private volatile Schedule? _schedule;

        public bool? IsMarketClosed(DateOnly date)
        {
            if (WeekdayTradingCalendar.IsWeekend(date)) return true;
            var schedule = _schedule;
            if (schedule is null || !schedule.Years.Contains(date.Year)) return null;
            return schedule.ClosedDays.Contains(date);
        }

        /// <summary>Loads the schedule from TWSE. Keeps the one already held when the fetch or its
        /// reading fails; true when a schedule was loaded.</summary>
        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            var schedule = await FetchAsync(ct);
            if (schedule is null) return false;
            _schedule = schedule;
            return true;
        }

        private async Task<Schedule?> FetchAsync(CancellationToken ct)
        {
            try
            {
                var client = httpClientFactory.CreateClient("ExternalReadOnly");
                using var req = new HttpRequestMessage(HttpMethod.Get, Url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var resp = await client.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                return Parse(await resp.Content.ReadAsStringAsync(ct), logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "TWSE holiday schedule fetch failed");
                return null;
            }
        }

        private static Schedule? Parse(string body, ILogger logger)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch (JsonException)
            {
                logger.LogWarning("TWSE holiday schedule is not JSON");
                return null;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("TWSE holiday schedule is not a JSON array");
                    return null;
                }

                var closed = new HashSet<DateOnly>();
                var years = new HashSet<int>();
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    if (!TryGetString(row, "Date", out var dateText) || !TryParseDate(dateText, out var date)) continue;

                    years.Add(date.Year);
                    var text = (TryGetString(row, "Name", out var n) ? n : "") + (TryGetString(row, "Description", out var d) ? d : "");
                    if (text.Contains("開始交易日") || text.Contains("最後交易日")) continue;
                    closed.Add(date);
                }

                if (years.Count == 0)
                {
                    logger.LogWarning("TWSE holiday schedule had no readable dates");
                    return null;
                }
                return new Schedule(closed, years);
            }
        }

        private static bool TryGetString(JsonElement row, string name, out string value)
        {
            value = "";
            foreach (var p in row.EnumerateObject())
            {
                if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind != JsonValueKind.String) continue;
                value = p.Value.GetString() ?? "";
                return true;
            }
            return false;
        }

        /// <summary>"1141001" / "114/10/01" (民國) or "20251001" / "2025/10/01" → a date.</summary>
        internal static bool TryParseDate(string text, out DateOnly date)
        {
            date = default;
            text = text.Trim();

            int year, month, day;
            if (text.Contains('/'))
            {
                var parts = text.Split('/');
                if (parts.Length != 3
                    || !int.TryParse(parts[0], out year) || !int.TryParse(parts[1], out month) || !int.TryParse(parts[2], out day))
                    return false;
                if (year < 1911) year += 1911;
            }
            else if (text.All(char.IsAsciiDigit) && text.Length is 7 or 8)
            {
                var yearLength = text.Length - 4;
                year = int.Parse(text[..yearLength]);
                month = int.Parse(text.Substring(yearLength, 2));
                day = int.Parse(text.Substring(yearLength + 2, 2));
                if (text.Length == 7) year += 1911;
            }
            else return false;

            if (year is < 1912 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            date = new DateOnly(year, month, day);
            return true;
        }
    }
}
