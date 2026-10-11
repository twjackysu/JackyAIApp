using System.Globalization;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>
    /// One reading of the TAIEX (加權指數, a price index) from TWSE's real-time service.
    /// </summary>
    /// <param name="TradeDate">The trading day the reading belongs to (MIS <c>d</c>).</param>
    /// <param name="QuotedAt">When the index was last computed, Taipei time (MIS <c>t</c> on <c>d</c>).</param>
    /// <param name="Last">The index now (MIS <c>z</c>).</param>
    /// <param name="PreviousClose">The previous trading day's close of the same index (MIS <c>y</c>).</param>
    public sealed record MisIndexQuote(DateOnly TradeDate, DateTimeOffset QuotedAt, decimal Last, decimal PreviousClose);

    /// <summary>
    /// Reads <c>mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw</c> (TASK-038). The
    /// service is undocumented and unauthenticated; like the MFI94U parser this one trusts only
    /// what it can name, and answers "no reading" — never a guess — for anything else, so a changed
    /// layout switches the estimate off instead of putting a wrong number on the card. The fields
    /// used are the ones the stock quotes of the same endpoint carry (<c>c</c> code, <c>d</c> date,
    /// <c>t</c> time, <c>tlong</c> epoch milliseconds, <c>z</c> last, <c>y</c> previous close); the
    /// build environment cannot reach the host, so that they are also on the index entry is checked
    /// by <c>TaiexContractTests</c> on the scheduled workflow.
    /// </summary>
    public static class MisTaiexParser
    {
        internal const string IndexCode = "t00";

        /// <summary>The largest one-day move believed, either way. The exchange halts at ±10% and the
        /// TAIEX's record is about ±9.7%, so a reading beyond this is bad data, not news.</summary>
        internal const decimal MaxMove = 0.15m;

        /// <summary>The reading, or null with the reason in <paramref name="rejection"/>.</summary>
        public static MisIndexQuote? Parse(string json, out string? rejection)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException) { rejection = "response is not JSON"; return null; }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("msgArray", out var rows) || rows.ValueKind != JsonValueKind.Array)
                {
                    rejection = "no msgArray";
                    return null;
                }
                if (rows.GetArrayLength() == 0)
                {
                    rejection = "msgArray is empty";
                    return null;
                }

                var row = rows[0];
                if (row.ValueKind != JsonValueKind.Object)
                {
                    rejection = "msgArray[0] is not an object";
                    return null;
                }

                // The answer must be about the index we asked for, not whatever came back.
                var code = Text(row, "c");
                var channel = Text(row, "ch");
                if (!string.Equals(code, IndexCode, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(channel, IndexCode + ".tw", StringComparison.OrdinalIgnoreCase))
                {
                    rejection = $"msgArray[0] is not {IndexCode} (c={code ?? "(missing)"}, ch={channel ?? "(missing)"})";
                    return null;
                }

                // "-" is MIS's word for "no trade yet" (before the open, or the first seconds of it).
                if (!TryPrice(Text(row, "z"), out var last))
                {
                    rejection = $"z (last) is not a usable price: {Text(row, "z") ?? "(missing)"}";
                    return null;
                }
                if (!TryPrice(Text(row, "y"), out var previous))
                {
                    rejection = $"y (previous close) is not a usable price: {Text(row, "y") ?? "(missing)"}";
                    return null;
                }

                var move = Math.Abs(last / previous - 1);
                if (move > MaxMove)
                {
                    rejection = $"z {last} vs y {previous} is a {move:P1} move (limit {MaxMove:P0})";
                    return null;
                }

                if (!TryDate(Text(row, "d"), out var tradeDate))
                {
                    rejection = $"d (date) is not yyyyMMdd: {Text(row, "d") ?? "(missing)"}";
                    return null;
                }
                if (!TryQuotedAt(row, tradeDate, out var quotedAt))
                {
                    rejection = "neither t (time) nor tlong (epoch ms) gives a time on the trade date";
                    return null;
                }

                rejection = null;
                return new MisIndexQuote(tradeDate, quotedAt, last, previous);
            }
        }

        private static string? Text(JsonElement row, string name) =>
            row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>A plain decimal above zero; "-", "", and anything with separators are not.</summary>
        private static bool TryPrice(string? text, out decimal value)
        {
            value = 0;
            return text is { Length: > 0 }
                && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                && value > 0;
        }

        private static bool TryDate(string? text, out DateOnly date)
        {
            date = default;
            return text is { Length: 8 }
                && DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>The time of day (<c>t</c>) on <paramref name="tradeDate"/>; failing that, the epoch
        /// milliseconds (<c>tlong</c>), which must fall on that same Taipei day.</summary>
        private static bool TryQuotedAt(JsonElement row, DateOnly tradeDate, out DateTimeOffset quotedAt)
        {
            var taipei = TimeSpan.FromHours(8); // Taiwan has no DST

            if (Text(row, "t") is { } time
                && TimeOnly.TryParseExact(time, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOfDay))
            {
                // 0001-01-01 is a valid date, but not one with a UTC+8 time of day on it.
                if (tradeDate.ToDateTime(timeOfDay).Ticks < taipei.Ticks) // would put the UTC time before year 1
                {
                    quotedAt = default;
                    return false;
                }
                quotedAt = new DateTimeOffset(tradeDate.ToDateTime(timeOfDay), taipei);
                return true;
            }

            if (Text(row, "tlong") is { } millis
                && long.TryParse(millis, NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
                && ms > 0 && ms < 4_102_444_800_000L) // before year 2100; anything later is not a clock reading
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(taipei);
                if (DateOnly.FromDateTime(at.DateTime) == tradeDate)
                {
                    quotedAt = at;
                    return true;
                }
            }

            quotedAt = default;
            return false;
        }
    }
}
