using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Demo controller that showcases all four DataAnalysisLibrary query types
    /// against the <c>dbo.Orders</c> table in the application SQL database.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class DataAnalysisController(
        ILogger<DataAnalysisController> logger,
        IMyResponseFactory responseFactory,
        DataAnalysisEngine engine) : ControllerBase
    {
        private readonly ILogger<DataAnalysisController> _logger = logger;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly DataAnalysisEngine _engine = engine;

        private const string SourceKey  = "mssql";
        private const string TableName  = "dbo.Orders";
        private const string DateField  = "OrderDate";

        // ──────────────────────────────────────────────────────────────────
        // 0. DistinctValues — list of unique values for a given column
        //    (used by the frontend filter LoV picker)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns up to 200 distinct values for the specified column of <c>dbo.Orders</c>,
        /// sorted alphabetically. Used to populate the filter value picker in the UI.
        /// </summary>
        [HttpGet("distinctvalues")]
        public async Task<IActionResult> GetDistinctValues(
            [FromQuery] string field,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(field))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "field query parameter is required.");

            _logger.LogInformation("DataAnalysis: DistinctValues for field={field}", field);

            // Reuse SummarizeAsync — GROUP BY field gives us all distinct values.
            var result = await _engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = SourceKey,
                TableName = TableName,
                ViewBy    = [new GroupField(field)],
                Measures  = [new MeasureField(field, AggregationType.Count, "_cnt")],
            }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("DistinctValues failed: {code} – {msg}", result.ErrorCode, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Query failed.");
            }

            var values = result.Value!.Rows
                .Select(r => r[field]?.ToString())
                .Where(v => v is not null && v.Length > 0)
                .Order()
                .Take(200)   // cap at 200 — LoV pickers with >200 entries are unusable
                .ToList();

            return _responseFactory.CreateOKResponse(new { values });
        }

        // ──────────────────────────────────────────────────────────────────
        // 1. FieldList — schema query
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the schema (column definitions) of <c>dbo.Orders</c>.
        /// Demonstrates the <c>FieldListAsync</c> query type.
        /// </summary>
        [HttpGet("schema")]
        public async Task<IActionResult> GetSchema(CancellationToken ct = default)
        {
            _logger.LogInformation("DataAnalysis: FieldList on {table}", TableName);

            var result = await _engine.FieldListAsync(new FieldListRequest
            {
                SourceKey = SourceKey,
                TableName = TableName,
            }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("FieldList failed: {code} – {msg}", result.ErrorCode, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Schema query failed.");
            }

            return _responseFactory.CreateOKResponse(result.Value);
        }

        // ──────────────────────────────────────────────────────────────────
        // 2. Summarize — aggregation query
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs a GroupBy + Measure aggregation on <c>dbo.Orders</c>.
        /// Demonstrates the <c>SummarizeAsync</c> query type.
        /// </summary>
        [HttpPost("summarize")]
        public async Task<IActionResult> Summarize(
            [FromBody] SummarizeRequestDto dto,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "DataAnalysis: Summarize viewBy=[{vb}] measures=[{m}]",
                string.Join(",", dto.ViewBy),
                string.Join(",", dto.Measures.Select(m => $"{m.FieldName}/{m.Aggregation}")));

            var result = await _engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = SourceKey,
                TableName = TableName,
                ViewBy   = dto.ViewBy.Select(f => new GroupField(f)).ToList(),
                Measures = dto.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation)).ToList(),
                Filters  = dto.Filters?.Select(f => new FilterDimension(f.FieldName, f.Values)).ToList() ?? [],
            }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Summarize failed: {code} – {msg}", result.ErrorCode, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Summarize query failed.");
            }

            // Flatten SummarizeRow wrappers to plain dictionaries for JSON serialisation.
            var rows = result.Value!.Rows.Select(r => r.Data).ToList();
            return _responseFactory.CreateOKResponse(new { rows });
        }

        // ──────────────────────────────────────────────────────────────────
        // 3. Details — row-level paginated query
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns paginated raw rows from <c>dbo.Orders</c>.
        /// Demonstrates the <c>DetailsAsync</c> query type.
        /// </summary>
        [HttpPost("details")]
        public async Task<IActionResult> Details(
            [FromBody] DetailsRequestDto dto,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "DataAnalysis: Details skip={s} take={t}", dto.Skip, dto.Take);

            var result = await _engine.DetailsAsync(new DetailsRequest
            {
                SourceKey    = SourceKey,
                TableName    = TableName,
                SelectFields = dto.SelectFields ?? [],
                OrderBy      = dto.OrderBy?.Select(o => new SortField(o.FieldName, o.Descending)).ToList() ?? [],
                Skip         = dto.Skip,
                Take         = Math.Min(dto.Take, 200), // hard cap at 200
                Filters      = dto.Filters?.Select(f => new FilterDimension(f.FieldName, f.Values)).ToList() ?? [],
            }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("Details failed: {code} – {msg}", result.ErrorCode, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Details query failed.");
            }

            return _responseFactory.CreateOKResponse(new
            {
                rows       = result.Value!.Rows,
                totalCount = result.Value!.TotalCount,
            });
        }

        // ──────────────────────────────────────────────────────────────────
        // 4. MaxDate — MAX() on a date column
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the maximum <c>OrderDate</c> in <c>dbo.Orders</c>.
        /// Demonstrates the <c>MaxDateAsync</c> query type.
        /// </summary>
        [HttpGet("maxdate")]
        public async Task<IActionResult> GetMaxDate(CancellationToken ct = default)
        {
            _logger.LogInformation("DataAnalysis: MaxDate on {field}", DateField);

            var result = await _engine.MaxDateAsync(new MaxDateRequest
            {
                SourceKey     = SourceKey,
                TableName     = TableName,
                DateFieldName = DateField,
            }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogError("MaxDate failed: {code} – {msg}", result.ErrorCode, result.ErrorMessage);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "MaxDate query failed.");
            }

            return _responseFactory.CreateOKResponse(new { maxDate = result.Value!.MaxDate });
        }
    }
}
