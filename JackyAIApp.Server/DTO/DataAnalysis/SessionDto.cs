using DataAnalysisLibrary.Core.Enums;

namespace JackyAIApp.Server.DTO.DataAnalysis
{
    /// <summary>Request body for uploading tabular data into a temporary in-memory session.</summary>
    public sealed record SessionUploadDto
    {
        /// <summary>Display name for this dataset, e.g. "Asset Trend 2025".</summary>
        public string? Name { get; init; }

        /// <summary>
        /// All cell values sent as strings. The server infers column types from the content.
        /// </summary>
        public required List<Dictionary<string, string>> Rows { get; init; }
    }

    /// <summary>Response returned after a successful session upload.</summary>
    public sealed record SessionUploadResponseDto
    {
        public required string          SessionId { get; init; }
        public required string          Name      { get; init; }
        public required int             RowCount  { get; init; }
        public required DateTimeOffset  ExpiresAt { get; init; }
        /// <summary>Inferred schema — same shape as FieldListResult.Fields so the frontend can reuse existing components.</summary>
        public required List<SessionFieldDefDto> Schema { get; init; }
    }

    public sealed record SessionFieldDefDto
    {
        public required string Name          { get; init; }
        public required string DataType      { get; init; }   // e.g. "nvarchar", "decimal", "datetime"
        public          bool   IsNullable    { get; init; } = true;
        public required string CanonicalType { get; init; }   // matches ColumnDataType enum string values
    }

    // ── Query results, as the client reads them ({ fields }, { maxDate }, ...) ──

    public sealed record SessionSchemaDto(List<SessionFieldDefDto> Fields);

    /// <summary>Latest value of a date column as ISO 8601, or null when the column has none.</summary>
    public sealed record SessionMaxDateDto(string? MaxDate);

    public sealed record SessionDistinctValuesDto(List<string> Values);

    public sealed record SessionRowsDto(List<Dictionary<string, object>> Rows);

    /// <summary>One page of matching rows, and how many matched in all.</summary>
    public sealed record SessionDetailsDto(List<Dictionary<string, object>> Rows, int TotalCount);

    /// <summary>Rows of a session for CSV export, and how many matched before the export cap.</summary>
    public sealed record SessionExport(
        string Name,
        IReadOnlyList<string> Headers,
        IReadOnlyList<Dictionary<string, string>> Rows,
        int TotalCount);

    // ── Internal cache entry ──────────────────────────────────────────────────

    internal sealed class SessionData
    {
        public required string                              Name      { get; init; }
        public required List<Dictionary<string, string>>   Rows      { get; init; }
        public required List<SessionFieldDefDto>           Schema    { get; init; }
        public required DateTimeOffset                     ExpiresAt { get; init; }
    }
}
