using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Maps DataAnalysis request DTOs to the library's query model. Shared by the DataAnalysis
    /// demo controller and saved dashboard widgets so the translation lives in one place.
    /// </summary>
    public static class DataAnalysisMapping
    {
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
