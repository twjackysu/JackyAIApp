using System.Globalization;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Pre-computes the relative date ranges an NL question can ask for ("latest month", "this year"…)
    /// from a date column's real latest value, so the LLM copies a ready-made range instead of doing
    /// calendar arithmetic. The small model got that arithmetic wrong — 「最新一個月」 with latest
    /// 2026-10-13 came back as 2026-09-01 ~ 2026-10-13 — even with the rule and a few-shot spelled out.
    /// </summary>
    public static class NlDateRanges
    {
        /// <summary>
        /// The DATA DATE RANGE line(s) for one date column: its latest date plus the relative ranges
        /// anchored on it. A value that can't be parsed as a date is passed through as just the latest.
        /// </summary>
        /// <param name="column">The date column's name.</param>
        /// <param name="latest">Its max value — "yyyy-MM-dd" from an engine, round-trip ("o") from a session.</param>
        public static string Describe(string column, string latest)
        {
            if (!DateTimeOffset.TryParse(latest, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return $"- {column}: latest = {latest}";

            // The clock date as written, not shifted to another time zone.
            var d = DateOnly.FromDateTime(parsed.DateTime);

            var monthStart   = new DateOnly(d.Year, d.Month, 1);
            var prevMonth    = monthStart.AddMonths(-1);
            var quarterStart = new DateOnly(d.Year, (d.Month - 1) / 3 * 3 + 1, 1);
            var prevQuarter  = quarterStart.AddMonths(-3);
            var yearStart    = new DateOnly(d.Year, 1, 1);

            return string.Join("\n",
                $"- {column}: latest = {F(d)}",
                $"    latest month (最新/最近一個月/本月/這個月): {F(monthStart)} ~ {F(d)}",
                $"    previous month (上個月): {F(prevMonth)} ~ {F(monthStart.AddDays(-1))}",
                $"    latest quarter (最新一季/本季): {F(quarterStart)} ~ {F(d)}",
                $"    previous quarter (上一季/上季): {F(prevQuarter)} ~ {F(quarterStart.AddDays(-1))}",
                $"    this year (今年): {F(yearStart)} ~ {F(d)}",
                $"    last year (去年): {F(yearStart.AddYears(-1))} ~ {F(yearStart.AddDays(-1))}");

            static string F(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
