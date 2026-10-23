using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.DTO.DataAnalysis
{
    // ── Source management ──────────────────────────────────────────────────────

    public sealed record DataSourceConfigResponseDto
    {
        public Guid                    Id               { get; init; }
        public required string         Name             { get; init; }
        public DataSourceConnectorType ConnectorType    { get; init; }
        /// <summary>Config with secrets masked (ClientSecret / Password shown as "***").</summary>
        public required object         Config           { get; init; }
        public string?                 DefaultTableName { get; init; }
        public DateTimeOffset          CreatedAt        { get; init; }
    }

    public sealed record SaveDataSourceConfigDto
    {
        public required string                Name             { get; init; }
        public required DataSourceConnectorType ConnectorType  { get; init; }
        /// <summary>Type-specific config — see MsSqlSourceConfigDto / AdxSourceConfigDto.</summary>
        public required object                 Config          { get; init; }
        public string?                         DefaultTableName { get; init; }
    }

    // ── Connector-specific config shapes ──────────────────────────────────────

    public sealed record MsSqlSourceConfigDto
    {
        public required string ConnectionString { get; init; }
    }

    public sealed record AdxSourceConfigDto
    {
        public required string ClusterUri   { get; init; }
        public required string Database     { get; init; }
        public string?         TenantId     { get; init; }
        public string?         ClientId     { get; init; }
        public string?         ClientSecret { get; init; }
        /// <summary>If set, overrides SP fields (for Managed Identity etc.).</summary>
        public string?         ConnectionString { get; init; }
    }

    // ── Shared query context ───────────────────────────────────────────────────

    /// <summary>
    /// Optional context added to every query request body.
    /// If SourceId is null the default engine (JackyAI SQL → dbo.Orders) is used.
    /// </summary>
    public sealed record QueryContextDto
    {
        public Guid?   SourceId  { get; init; }
        public string? TableName { get; init; }
    }

    // ── GroupField DTOs ───────────────────────────────────────────────────────

    /// <summary>
    /// Discriminated-union DTO for all GroupFieldBase subtypes.
    /// <c>Type</c> is "Plain" | "DateTrunc" | "NumericBin" | "StringGrouping".
    /// </summary>
    public sealed record GroupFieldDto
    {
        public string  Type        { get; init; } = "Plain";
        public required string FieldName { get; init; }
        public string? Alias       { get; init; }
        /// <summary>DateTrunc only — "Year" | "Quarter" | "Month" | "Week" | "Day" | "Hour".</summary>
        public string? Granularity { get; init; }
        /// <summary>NumericBin / StringGrouping — label for unmatched rows (default "Other").</summary>
        public string? Fallback    { get; init; }
        /// <summary>NumericBin only.</summary>
        public List<NumericBinDto>?   Bins   { get; init; }
        /// <summary>StringGrouping only.</summary>
        public List<StringGroupDto>?  Groups { get; init; }
    }

    public sealed record NumericBinDto
    {
        public double? Min   { get; init; }
        public double? Max   { get; init; }
        public required string Label { get; init; }
    }

    public sealed record StringGroupDto
    {
        public required string       Label  { get; init; }
        public required List<string> Values { get; init; }
    }

    // ── Query request DTOs ────────────────────────────────────────────────────

    public sealed record SummarizeRequestDto
    {
        public QueryContextDto?               Context  { get; init; }
        public required List<GroupFieldDto>   ViewBy   { get; init; }
        public required List<MeasureFieldDto> Measures { get; init; }
        public List<FilterDimensionDto>?      Filters  { get; init; }
    }

    public sealed record MeasureFieldDto
    {
        public required string          FieldName   { get; init; }
        public AggregationType          Aggregation { get; init; }
        /// <summary>Output column alias. Defaults to FieldName when omitted.</summary>
        public string?                  Alias       { get; init; }
    }

    public sealed record DetailsRequestDto
    {
        public QueryContextDto?          Context      { get; init; }
        public List<string>?             SelectFields { get; init; }
        public List<SortFieldDto>?       OrderBy      { get; init; }
        public int                       Skip         { get; init; } = 0;
        public int                       Take         { get; init; } = 50;
        public List<FilterDimensionDto>? Filters      { get; init; }
    }

    public sealed record SortFieldDto
    {
        public required string FieldName   { get; init; }
        public bool            Descending  { get; init; } = false;
    }

    public sealed record FilterDimensionDto
    {
        public required string       FieldName { get; init; }
        public required List<string> Values    { get; init; }
        /// <summary>
        /// Filter operator: "In" | "NotIn" | "Equals" | "GreaterThan" | "LessThan" | "Between" | "Contains".
        /// Defaults to "In" when absent or unrecognised.
        /// </summary>
        public string? Op { get; init; }
    }

    // ── NL Summarize ─────────────────────────────────────────────────────────

    /// <summary>Request body for the natural-language summarize endpoint.</summary>
    public sealed record NlSummarizeRequestDto
    {
        public QueryContextDto? Context   { get; init; }
        public string?          SessionId { get; init; }
        /// <summary>Free-text user query, e.g. "ARR by Account Segment for TW region".</summary>
        public required string  Query     { get; init; }
    }

    /// <summary>
    /// The plan as the LLM writes it: a <see cref="SummarizeRequestDto"/> plus the conditions it could
    /// not map onto the data. Kept apart so <see cref="Unmatched"/> never reaches an executed or saved
    /// plan — it only becomes a note for the user.
    /// </summary>
    public sealed record NlPlanDto
    {
        public required List<GroupFieldDto>   ViewBy    { get; init; }
        public required List<MeasureFieldDto> Measures  { get; init; }
        public List<FilterDimensionDto>?      Filters   { get; init; }
        /// <summary>
        /// The user's own wording for each filter condition no real value matches (e.g. 「越南」 when
        /// Country holds no Vietnam). Without it the LLM drops such a condition silently and the
        /// unfiltered result reads like the answer.
        /// </summary>
        public List<string>?                  Unmatched { get; init; }

        public SummarizeRequestDto ToSummarizeRequest() =>
            new() { ViewBy = ViewBy, Measures = Measures, Filters = Filters };
    }

    /// <summary>Response from the natural-language summarize endpoint.</summary>
    public sealed record NlSummarizeResponseDto
    {
        /// <summary>The SummarizeRequestDto parameters the LLM generated (for display / audit).</summary>
        public required SummarizeRequestDto Plan           { get; init; }
        /// <summary>One-liner describing what the AI understood.</summary>
        public string?                       Interpretation { get; init; }
        /// <summary>The aggregated result rows.</summary>
        public required IEnumerable<object>  Rows           { get; init; }
        /// <summary>
        /// Soft caveats to surface to the user, e.g. a requested filter value that didn't exist in the
        /// data and was therefore ignored. Empty/null when the plan matched the question cleanly.
        /// </summary>
        public List<string>?                 Notes          { get; init; }
        /// <summary>
        /// The user's wording for each condition the AI could not map onto the data and therefore left
        /// out of the plan (each also has a note). Null when every condition was applied.
        /// </summary>
        public List<string>?                 Unmatched      { get; init; }
    }

    // ── Engine query answers ─────────────────────────────────────────────────

    /// <summary>The latest value of a date column; null when the table or the column holds none.</summary>
    public sealed record MaxDateDto(DateTimeOffset? MaxDate);

    /// <summary>A column's distinct values for the filter picker, empty ones left out.</summary>
    public sealed record DistinctValuesDto(List<string> Values);

    /// <summary>The aggregated rows of a summarize.</summary>
    public sealed record SummarizeRowsDto(IEnumerable<IReadOnlyDictionary<string, object?>> Rows);

    /// <summary>One page of matching rows, and how many matched in all.</summary>
    public sealed record DetailsPageDto(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount);
}
