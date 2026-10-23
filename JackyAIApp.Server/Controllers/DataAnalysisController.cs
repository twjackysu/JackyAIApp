using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Extensions;
using JackyAIApp.Server.Services.DataAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// The Data Analysis page: queries on the built-in default source (dbo.Orders via JackyAI SQL), on a
    /// signed-in user's saved custom source (SQL Server or Azure Data Explorer), or on an uploaded session.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class DataAnalysisController(
        IMyResponseFactory responseFactory,
        IDataSourceStore sources,
        ISessionDataStore sessionStore,
        IDataAnalysisQueryService queries,
        INlSummarizeService nlSummarize) : ControllerBase
    {
        private readonly IMyResponseFactory         _responseFactory = responseFactory;
        private readonly IDataSourceStore           _sources         = sources;
        private readonly ISessionDataStore          _sessionStore    = sessionStore;
        private readonly IDataAnalysisQueryService  _queries         = queries;
        private readonly INlSummarizeService        _nlSummarize     = nlSummarize;

        // ═══════════════════════════════════════════════════════════════════════
        // Source CRUD
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>List the current user's saved data-source connectors.</summary>
        [Authorize]
        [HttpGet("sources")]
        public async Task<IActionResult> GetSources(CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _sources.ListAsync(ct));

        /// <summary>Save a new data-source connector for the current user.</summary>
        [Authorize]
        [HttpPost("sources")]
        public async Task<IActionResult> CreateSource(
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _sources.CreateAsync(dto, ct));

        /// <summary>Update one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpPut("sources/{id:guid}")]
        public async Task<IActionResult> UpdateSource(
            Guid id,
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _sources.UpdateAsync(id, dto, ct));

        /// <summary>Delete one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpDelete("sources/{id:guid}")]
        public async Task<IActionResult> DeleteSource(Guid id, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _sources.DeleteAsync(id, ct));

        // ═══════════════════════════════════════════════════════════════════════
        // Query endpoints  (all accept optional QueryContext for custom source)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>The schema of the target table. Pass sourceId + tableName to use a custom source.</summary>
        [HttpGet("schema")]
        public async Task<IActionResult> GetSchema(
            [FromQuery] Guid?   sourceId  = null,
            [FromQuery] string? tableName = null,
            CancellationToken   ct        = default) =>
            _responseFactory.ToActionResult(await _queries.GetSchemaAsync(Context(sourceId, tableName), ct));

        /// <summary>The MAX value of the date field.</summary>
        [HttpGet("maxdate")]
        public async Task<IActionResult> GetMaxDate(
            [FromQuery] Guid?   sourceId   = null,
            [FromQuery] string? tableName  = null,
            [FromQuery] string  dateField  = "OrderDate",
            CancellationToken   ct         = default) =>
            _responseFactory.ToActionResult(await _queries.GetMaxDateAsync(Context(sourceId, tableName), dateField, ct));

        /// <summary>Up to 200 distinct values for a column (the filter LoV picker), in the column's natural order.</summary>
        [HttpGet("distinctvalues")]
        public async Task<IActionResult> GetDistinctValues(
            [FromQuery] string  field,
            [FromQuery] Guid?   sourceId  = null,
            [FromQuery] string? tableName = null,
            CancellationToken   ct        = default) =>
            _responseFactory.ToActionResult(await _queries.GetDistinctValuesAsync(Context(sourceId, tableName), field, ct));

        /// <summary>GroupBy + Measures aggregation.</summary>
        [HttpPost("summarize")]
        public async Task<IActionResult> Summarize([FromBody] SummarizeRequestDto dto, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _queries.SummarizeAsync(dto, ct));

        /// <summary>Paginated row-level data.</summary>
        [HttpPost("details")]
        public async Task<IActionResult> Details([FromBody] DetailsRequestDto dto, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _queries.DetailsAsync(dto, ct));

        /// <summary>
        /// Every row matching the filters, in the requested order, as a CSV download (see
        /// <see cref="IDataAnalysisQueryService.ExportAsync"/>). Skip/Take in the body are ignored.
        /// </summary>
        [HttpPost("details/export")]
        public async Task<IActionResult> ExportDetails([FromBody] DetailsRequestDto dto, CancellationToken ct = default) =>
            Csv(await _queries.ExportAsync(dto, export => WriteCsvAsync(export, ct), ct));

        /// <summary>
        /// Natural-language summarize (see <see cref="INlSummarizeService"/>): the plan the AI made, what
        /// it understood, the rows, and the conditions it could not apply. Requires login — every call
        /// consumes usage budget.
        /// </summary>
        [HttpPost("summarize/nl")]
        [Authorize]
        [UsageLimit(UsageFeature.NlSummarize)]
        public async Task<IActionResult> NlSummarize([FromBody] NlSummarizeRequestDto dto, CancellationToken ct = default) =>
            _responseFactory.ToActionResult(await _nlSummarize.SummarizeAsync(dto, ct));

        private static QueryContextDto Context(Guid? sourceId, string? tableName) =>
            new() { SourceId = sourceId, TableName = tableName };

        // ═══════════════════════════════════════════════════════════════════════
        // Session upload — temporary in-memory data source (see ISessionDataStore).
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upload tabular data into a temporary in-memory session (expires in 2h, never persisted). Stays
        /// anonymous, so it is bounded on every side: a rate limit (signed-in users each, anonymous callers
        /// together), a pre-binding check of size, session budget and memory load, a body-size cap for
        /// bodies without Content-Length, and the store's shape and memory limits (see SessionUploadOptions).
        /// </summary>
        [HttpPost("session")]
        [EnableRateLimiting(RateLimitPolicies.SessionUpload)]
        [ServiceFilter(typeof(SessionUploadAdmissionFilter))]
        [RequestSizeLimit(SessionUploadOptions.MaxRequestBodyBytes)]
        public IActionResult UploadSession([FromBody] SessionUploadDto dto) =>
            _responseFactory.ToActionResult(_sessionStore.Upload(dto));

        /// <summary>Returns the schema of a session.</summary>
        [HttpGet("session/{sessionId}/schema")]
        public IActionResult GetSessionSchema(string sessionId) =>
            _responseFactory.ToActionResult(_sessionStore.GetSchema(sessionId));

        /// <summary>MAX value of a date column in the session data.</summary>
        [HttpGet("session/{sessionId}/maxdate")]
        public IActionResult GetSessionMaxDate(string sessionId, [FromQuery] string dateField = "Date") =>
            _responseFactory.ToActionResult(_sessionStore.GetMaxDate(sessionId, dateField));

        /// <summary>Distinct values for a column in the session data (for filter dropdowns).</summary>
        [HttpGet("session/{sessionId}/distinctvalues")]
        public IActionResult GetSessionDistinctValues(string sessionId, [FromQuery] string field) =>
            _responseFactory.ToActionResult(_sessionStore.GetDistinctValues(sessionId, field));

        /// <summary>GroupBy + Measures aggregation on session data.</summary>
        [HttpPost("session/{sessionId}/summarize")]
        public IActionResult SessionSummarize(string sessionId, [FromBody] SummarizeRequestDto dto) =>
            _responseFactory.ToActionResult(_sessionStore.Summarize(sessionId, dto));

        /// <summary>Paginated row-level data for a session.</summary>
        [HttpPost("session/{sessionId}/details")]
        public IActionResult SessionDetails(string sessionId, [FromBody] DetailsRequestDto dto) =>
            _responseFactory.ToActionResult(_sessionStore.Details(sessionId, dto));

        /// <summary>Every matching row of a session as a CSV download (see <see cref="ExportDetails"/>).</summary>
        [HttpPost("session/{sessionId}/details/export")]
        public async Task<IActionResult> SessionExportDetails(
            string sessionId,
            [FromBody] DetailsRequestDto dto,
            CancellationToken ct = default) =>
            Csv(await _queries.ExportSessionAsync(sessionId, dto, export => WriteCsvAsync(export, ct)));

        // ── CSV download ───────────────────────────────────────────────────────

        /// <summary>The file has been written to the response already; a failure is answered as usual.</summary>
        private IActionResult Csv(ServiceResult<NoData> result) =>
            result.IsSuccess ? new EmptyResult() : _responseFactory.ToActionResult(result);

        /// <summary>
        /// Streams a CSV download. The counts go in headers so the client can say when the export
        /// was cut off at <see cref="DataAnalysisQueryService.MaxExportRows"/>.
        /// </summary>
        private async Task WriteCsvAsync(CsvExport export, CancellationToken ct)
        {
            var safeName = string.Concat(export.Name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
            var fileName = $"{(safeName.Length > 0 ? safeName : "export")}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = fileName,
            }.ToString();
            Response.Headers["X-Exported-Count"] = export.ExportedCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Total-Count"]    = export.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

            await CsvExportWriter.WriteAsync(Response.Body, export.Headers, export.Rows, ct);
        }
    }
}
