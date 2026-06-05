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

    // ── Internal cache entry ──────────────────────────────────────────────────

    internal sealed class SessionData
    {
        public required string                              Name      { get; init; }
        public required List<Dictionary<string, string>>   Rows      { get; init; }
        public required List<SessionFieldDefDto>           Schema    { get; init; }
        public required DateTimeOffset                     ExpiresAt { get; init; }
    }
}
