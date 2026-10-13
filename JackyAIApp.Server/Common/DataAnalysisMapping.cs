using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using System.Globalization;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Maps DataAnalysis request DTOs to the library's query model. Shared by the DataAnalysis
    /// demo controller and saved dashboard widgets so the translation lives in one place.
    /// </summary>
    public static class DataAnalysisMapping
    {
        /// <summary>
        /// Maps a request's filters to the library's model, after <see cref="ExpandDateOnlyRangeEnds"/>.
        /// Use this rather than mapping filters one by one.
        /// </summary>
        public static List<FilterDimension> MapFilters(IEnumerable<FilterDimensionDto>? filters) =>
            ExpandDateOnlyRangeEnds(filters).Select(MapFilter).ToList();

        /// <summary>
        /// Makes a date-only Between end inclusive of that whole day. A timestamp column compared with
        /// "2026-10-13" sees 2026-10-13 00:00:00, so <c>BETWEEN '2026-10-12' AND '2026-10-13'</c> drops
        /// every order placed later on the 13th — and the NL planner always ends relative ranges on the
        /// data's latest date. Such a filter becomes <c>Between [start, end+1]</c> AND <c>LessThan end+1</c>,
        /// i.e. start ≤ x &lt; end+1, which the library has no single operator for. Filters are ANDed by
        /// every connector and by the session store, so the pair is exact on date, datetime and
        /// date-like string columns alike.
        /// </summary>
        public static List<FilterDimensionDto> ExpandDateOnlyRangeEnds(IEnumerable<FilterDimensionDto>? filters)
        {
            var result = new List<FilterDimensionDto>();
            foreach (var f in filters ?? [])
            {
                if (f.Op == "Between" && f.Values.Count >= 2 &&
                    DateOnly.TryParseExact(f.Values[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    var nextDay = end.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    result.Add(f with { Values = [f.Values[0], nextDay] });
                    result.Add(f with { Op = "LessThan", Values = [nextDay] });
                }
                else
                {
                    result.Add(f);
                }
            }
            return result;
        }

        /// <summary>Maps a <see cref="FilterDimensionDto"/> to a <see cref="FilterDimension"/>, honouring the optional Op.</summary>
        public static FilterDimension MapFilter(FilterDimensionDto f)
        {
            var op = f.Op switch
            {
                "NotIn" => FilterOperator.NotIn,
                "Equals" => FilterOperator.Equals,
                "GreaterThan" => FilterOperator.GreaterThan,
                "LessThan" => FilterOperator.LessThan,
                "Between" => FilterOperator.Between,
                "Contains" => FilterOperator.Contains,
                _ => FilterOperator.In, // default, also handles "In" and null
            };
            return new FilterDimension(f.FieldName, f.Values, op);
        }

        /// <summary>Maps a <see cref="GroupFieldDto"/> to the appropriate <see cref="GroupFieldBase"/> subtype.</summary>
        public static GroupFieldBase MapGroupField(GroupFieldDto f) => f.Type switch
        {
            "DateTrunc" => new DateTruncGroupField(
                f.FieldName,
                Enum.Parse<DateGranularity>(f.Granularity ?? "Day"),
                NullIfEmpty(f.Alias)),

            "NumericBin" => new NumericBinGroupField(
                f.FieldName,
                (f.Bins ?? []).Select(b => new NumericBin(b.Min, b.Max, b.Label)).ToList(),
                NullIfEmpty(f.Alias),
                f.Fallback ?? "Other"),

            "StringGrouping" => new StringGroupingField(
                f.FieldName,
                (f.Groups ?? []).Select(g => new StringGroup(g.Label, g.Values)).ToList(),
                NullIfEmpty(f.Alias),
                f.Fallback ?? "Other"),

            _ => new GroupField(f.FieldName, NullIfEmpty(f.Alias)),
        };

        public static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
