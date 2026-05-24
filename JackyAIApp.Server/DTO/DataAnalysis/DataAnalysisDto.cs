using DataAnalysisLibrary.Core.Enums;

namespace JackyAIApp.Server.DTO.DataAnalysis
{
    /// <summary>Request body for the Summarize endpoint.</summary>
    public sealed record SummarizeRequestDto
    {
        /// <summary>Fields to group by.</summary>
        public required List<string> ViewBy { get; init; }

        /// <summary>Measures to compute for each group.</summary>
        public required List<MeasureFieldDto> Measures { get; init; }

        /// <summary>Optional dimension filters.</summary>
        public List<FilterDimensionDto>? Filters { get; init; }
    }

    /// <summary>A single measure: field + aggregation type.</summary>
    public sealed record MeasureFieldDto
    {
        public required string FieldName { get; init; }
        public AggregationType Aggregation { get; init; }
    }

    /// <summary>Request body for the Details endpoint.</summary>
    public sealed record DetailsRequestDto
    {
        /// <summary>Columns to return; empty = all columns.</summary>
        public List<string>? SelectFields { get; init; }

        /// <summary>Sort order.</summary>
        public List<SortFieldDto>? OrderBy { get; init; }

        /// <summary>Rows to skip (pagination).</summary>
        public int Skip { get; init; } = 0;

        /// <summary>Max rows to return.</summary>
        public int Take { get; init; } = 50;

        /// <summary>Optional dimension filters.</summary>
        public List<FilterDimensionDto>? Filters { get; init; }
    }

    /// <summary>A single sort rule.</summary>
    public sealed record SortFieldDto
    {
        public required string FieldName { get; init; }
        public bool Descending { get; init; } = false;
    }

    /// <summary>A dimension filter: field + list of accepted values.</summary>
    public sealed record FilterDimensionDto
    {
        public required string FieldName { get; init; }
        public required List<string> Values { get; init; }
    }
}
