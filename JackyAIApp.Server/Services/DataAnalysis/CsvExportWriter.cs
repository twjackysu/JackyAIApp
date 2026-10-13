using System.Globalization;
using System.Text;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Writes Data Analysis rows as a downloadable CSV: UTF-8 with a BOM (so Excel shows Chinese),
    /// CRLF line ends, RFC 4180 quoting, and culture-invariant numbers and dates.
    /// <para>
    /// Text cells that would start a spreadsheet formula (=, +, -, @, tab, CR) get a leading
    /// apostrophe, so opening an export in Excel never evaluates data as a formula. Text that is
    /// itself a number ("-5", "+3.2") is left alone: session uploads keep every value as text.
    /// </para>
    /// </summary>
    public static class CsvExportWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Writes the header row, then each row's cells in the same order as the headers.</summary>
        public static async Task WriteAsync(
            Stream output,
            IReadOnlyList<string> headers,
            IEnumerable<IEnumerable<object?>> rows,
            CancellationToken ct = default)
        {
            await using var writer = new StreamWriter(
                output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
            };

            var line = new StringBuilder();
            await writer.WriteLineAsync(Join(line, headers));
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(Join(line, row));
            }
            await writer.FlushAsync(ct);
        }

        private static string Join(StringBuilder line, IEnumerable<object?> cells)
        {
            line.Clear();
            var first = true;
            foreach (var cell in cells)
            {
                if (!first) line.Append(',');
                line.Append(Cell(cell));
                first = false;
            }
            return line.ToString();
        }

        internal static string Cell(object? value) => value switch
        {
            null or DBNull   => "",
            string s         => Quote(GuardFormula(s)),
            bool b           => b ? "true" : "false",
            // Dates without a time of day (SQL date columns come back as midnight) stay date-only.
            DateTime d       => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", Inv) : d.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            DateTimeOffset o => o.Offset == TimeSpan.Zero ? o.ToString("yyyy-MM-dd HH:mm:ss", Inv) : o.ToString("yyyy-MM-dd HH:mm:sszzz", Inv),
            DateOnly d       => d.ToString("yyyy-MM-dd", Inv),
            byte[] bytes     => Convert.ToBase64String(bytes),
            IFormattable f   => Quote(f.ToString(null, Inv)),
            _                => Quote(GuardFormula(value.ToString() ?? "")),
        };

        private static string GuardFormula(string s) =>
            s.Length > 0
            && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !double.TryParse(s, NumberStyles.Float, Inv, out _)
                ? "'" + s
                : s;

        private static string Quote(string s) =>
            s.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
