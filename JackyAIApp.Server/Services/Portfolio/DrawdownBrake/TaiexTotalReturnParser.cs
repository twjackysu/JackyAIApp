using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// Parses TWSE's MFI94U (發行量加權股價報酬指數) month response:
    /// <c>{"stat":"OK","fields":[...],"data":[["115/10/08","123,456.78"], ...]}</c>.
    ///
    /// The columns are found by their header text rather than by position, and a header that is
    /// missing or matches more than one column fails the whole month. Headers are compared with
    /// their whitespace removed: the live date header is "日　期", with a full-width space (U+3000)
    /// inside, which is how this was found. A wrong column here would
    /// not crash anything: it would feed a plausible-looking but wrong series into the signal,
    /// which is the one failure this parser exists to prevent. The same goes for a row it cannot
    /// read — nothing from a month is returned unless every row in it parsed.
    /// </summary>
    internal static class TaiexTotalReturnParser
    {
        private const string DateHeader = "日期";
        private const string ValueHeader = "報酬指數";

        public static TaiexMonthResult Parse(string body, int year, int month, ILogger logger)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                // An HTML page (rate limit, maintenance) rather than JSON. Treated as a failure
                // so the refresh stops asking instead of hammering a host that is pushing back.
                logger.LogWarning("MFI94U {Year}-{Month:00}: response is not JSON (first 120 chars: {Head})",
                    year, month, body[..Math.Min(body.Length, 120)]);
                return TaiexMonthResult.Failure;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("stat", out var stat) || stat.ValueKind != JsonValueKind.String)
                {
                    logger.LogWarning("MFI94U {Year}-{Month:00}: no \"stat\" in response", year, month);
                    return TaiexMonthResult.Failure;
                }

                // "很抱歉，沒有符合條件的資料!" and friends: a month with nothing published yet.
                if (!string.Equals(stat.GetString(), "OK", StringComparison.OrdinalIgnoreCase))
                    return TaiexMonthResult.Ok([]);

                if (!root.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array
                    || !root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("MFI94U {Year}-{Month:00}: stat OK but no fields/data arrays", year, month);
                    return TaiexMonthResult.Failure;
                }

                if (dataEl.GetArrayLength() == 0) return TaiexMonthResult.Ok([]);

                var fields = fieldsEl.EnumerateArray()
                    .Select(f => f.ValueKind == JsonValueKind.String ? WithoutWhitespace(f.GetString() ?? "") : "")
                    .ToList();
                var dateCols = IndexesContaining(fields, DateHeader);
                var valueCols = IndexesContaining(fields, ValueHeader);
                if (dateCols.Count != 1 || valueCols.Count != 1)
                {
                    logger.LogWarning(
                        "MFI94U {Year}-{Month:00}: expected exactly one \"{DateHeader}\" and one \"{ValueHeader}\" column, found {Dates} and {Values}; fields were [{Fields}]",
                        year, month, DateHeader, ValueHeader, dateCols.Count, valueCols.Count, string.Join(" | ", fields));
                    return TaiexMonthResult.Failure;
                }
                var dateIdx = dateCols[0];
                var valueIdx = valueCols[0];

                var points = new Dictionary<DateOnly, decimal>();
                foreach (var row in dataEl.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= Math.Max(dateIdx, valueIdx))
                    {
                        logger.LogWarning("MFI94U {Year}-{Month:00}: a data row is not an array of {Count}+ cells", year, month, Math.Max(dateIdx, valueIdx) + 1);
                        return TaiexMonthResult.Failure;
                    }

                    var dateText = CellText(row[dateIdx]);
                    var valueText = CellText(row[valueIdx]);

                    if (!TryParseRocDate(dateText, out var date) || date.Year != year || date.Month != month)
                    {
                        logger.LogWarning("MFI94U {Year}-{Month:00}: unreadable or out-of-month date \"{Date}\"", year, month, dateText);
                        return TaiexMonthResult.Failure;
                    }

                    if (valueText is "--" or "-" or "") continue; // no value published for that day

                    if (!TryParseNumber(valueText, out var close) || close <= 0)
                    {
                        logger.LogWarning("MFI94U {Year}-{Month:00}: unreadable index value \"{Value}\" on {Date}", year, month, valueText, dateText);
                        return TaiexMonthResult.Failure;
                    }

                    if (!points.TryAdd(date, close))
                    {
                        logger.LogWarning("MFI94U {Year}-{Month:00}: {Date} appears twice", year, month, dateText);
                        return TaiexMonthResult.Failure;
                    }
                }

                return TaiexMonthResult.Ok(points
                    .OrderBy(p => p.Key)
                    .Select(p => new IndexClose(p.Key, p.Value))
                    .ToList());
            }
        }

        /// <summary>Drops every kind of whitespace, full-width included (<see cref="char.IsWhiteSpace(char)"/>
        /// covers U+3000).</summary>
        private static string WithoutWhitespace(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        private static List<int> IndexesContaining(List<string> fields, string header) =>
            fields.Select((f, i) => (f, i)).Where(x => x.f.Contains(header)).Select(x => x.i).ToList();

        private static string CellText(JsonElement cell) =>
            cell.ValueKind == JsonValueKind.String ? (cell.GetString() ?? "").Trim() : cell.ToString().Trim();

        /// <summary>"115/10/08" (民國 115 = 2026) → 2026-10-08.</summary>
        internal static bool TryParseRocDate(string text, out DateOnly date)
        {
            date = default;
            var parts = text.Split('/');
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rocYear)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day))
                return false;

            var year = rocYear + 1911;
            if (rocYear < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            date = new DateOnly(year, month, day);
            return true;
        }

        /// <summary>"26,123.45" → 26123.45. Thousands separators only; any other text is rejected.</summary>
        internal static bool TryParseNumber(string text, out decimal value) =>
            decimal.TryParse(text.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
