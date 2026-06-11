using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Controllers
{
    /// <summary>
    /// Demo controller that showcases all four DataAnalysisLibrary query types.
    /// Supports a built-in default source (dbo.Orders via JackyAI SQL) plus
    /// user-saved custom sources (SQL Server or Azure Data Explorer).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class DataAnalysisController(
        ILogger<DataAnalysisController> logger,
        IMyResponseFactory responseFactory,
        DataAnalysisEngine defaultEngine,
        AzureSQLDBContext dbContext,
        IDataSourceConfigService configService,
        ISessionDataStore sessionStore,
        IOpenAIPromptService openAIPromptService,
        ICreditService creditService,
        IPromptLoader promptLoader) : ApiControllerBase
    {
        private readonly ILogger<DataAnalysisController> _logger               = logger;
        private readonly IMyResponseFactory              _responseFactory       = responseFactory;
        private readonly DataAnalysisEngine              _defaultEngine         = defaultEngine;
        private readonly AzureSQLDBContext               _dbContext             = dbContext;
        private readonly IDataSourceConfigService        _configService         = configService;
        private readonly ISessionDataStore               _sessionStore          = sessionStore;
        private readonly IOpenAIPromptService            _openAIPromptService   = openAIPromptService;
        private readonly ICreditService                  _creditService         = creditService;
        private readonly IPromptLoader                   _promptLoader          = promptLoader;

        // ── Default source constants ───────────────────────────────────────────
        private const string DefaultMsSqlKey   = "mssql";
        private const string DefaultTableName  = "dbo.Orders";

        // ── Engine resolution ──────────────────────────────────────────────────

        /// <summary>
        /// Resolved query context.  When <c>OwnsEngine</c> is <c>true</c> the engine was
        /// built on-the-fly for a custom source and <em>must</em> be disposed after the
        /// request completes to release the underlying connector resources (e.g. Kusto client).
        /// The singleton default engine has <c>OwnsEngine = false</c> and must NOT be disposed
        /// here — its lifetime is managed by the DI container.
        /// </summary>
        private sealed record ResolvedCtx(
            DataAnalysisEngine Engine,
            string             SourceKey,
            string             TableName,
            bool               OwnsEngine) : IDisposable
        {
            public void Dispose() { if (OwnsEngine) Engine.Dispose(); }
        }

        private async Task<ResolvedCtx?> TryResolveAsync(
            QueryContextDto? ctx, CancellationToken ct)
        {
            if (ctx?.SourceId is null)
                // The default source is the application's own database. Never honor a
                // caller-supplied table name here — it is pinned to the demo table so
                // the default source cannot be used to read arbitrary app tables.
                return new ResolvedCtx(_defaultEngine, DefaultMsSqlKey, DefaultTableName, OwnsEngine: false);

            var userId = CurrentUserId;
            // Custom sources require an authenticated user who owns the record.
            if (userId is null) return null;

            var source = await _dbContext.DataSourceConfigs
                .FirstOrDefaultAsync(s => s.Id == ctx.SourceId.Value && s.UserId == userId, ct);
            if (source is null) return null;

            var engine = _configService.BuildEngine(source);
            var sourceKey = _configService.SourceKey(source.ConnectorType);
            var tableName = ctx.TableName ?? source.DefaultTableName ?? "";
            return new ResolvedCtx(engine, sourceKey, tableName, OwnsEngine: true);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Source CRUD
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>List the current user's saved data-source connectors.</summary>
        [Authorize]
        [HttpGet("sources")]
        public async Task<IActionResult> GetSources(CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var sources = await _dbContext.DataSourceConfigs
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            return _responseFactory.CreateOKResponse(sources.Select(_configService.ToResponseDto).ToList());
        }

        /// <summary>Save a new data-source connector for the current user.</summary>
        [Authorize]
        [HttpPost("sources")]
        public async Task<IActionResult> CreateSource(
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var entity = new DataSourceConfig
            {
                Id               = Guid.NewGuid(),
                UserId           = userId,
                Name             = dto.Name,
                ConnectorType    = dto.ConnectorType,
                ConfigJson       = _configService.SerializeAndEncrypt(dto.Config, dto.ConnectorType),
                DefaultTableName = dto.DefaultTableName,
                CreatedAt        = DateTimeOffset.UtcNow,
            };
            _dbContext.DataSourceConfigs.Add(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Created DataSourceConfig {id} ({name}) for user {uid}", entity.Id, entity.Name, userId);
            return _responseFactory.CreateOKResponse(_configService.ToResponseDto(entity));
        }

        /// <summary>Update one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpPut("sources/{id:guid}")]
        public async Task<IActionResult> UpdateSource(
            Guid id,
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var entity = await _dbContext.DataSourceConfigs
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
            if (entity is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            entity.Name             = dto.Name;
            entity.ConnectorType    = dto.ConnectorType;
            entity.DefaultTableName = dto.DefaultTableName;
            entity.UpdatedAt        = DateTimeOffset.UtcNow;

            // Merge: non-secret fields always update; secret fields only update when
            // the client sent a real value (not the masked placeholder "***").
            entity.ConfigJson = _configService.MergeAndEncrypt(dto.Config, entity, dto.ConnectorType);

            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse(_configService.ToResponseDto(entity));
        }

        /// <summary>Delete one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpDelete("sources/{id:guid}")]
        public async Task<IActionResult> DeleteSource(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId;
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var entity = await _dbContext.DataSourceConfigs
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
            if (entity is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            _dbContext.DataSourceConfigs.Remove(entity);
            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Query endpoints  (all accept optional QueryContext for custom source)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns the schema of the target table (FieldListAsync).
        /// Pass sourceId + tableName as query parameters to use a custom source.
        /// </summary>
        [HttpGet("schema")]
        public async Task<IActionResult> GetSchema(
            [FromQuery] Guid?   sourceId  = null,
            [FromQuery] string? tableName = null,
            CancellationToken   ct        = default)
        {
            using var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
            if (ctx is null) return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            _logger.LogInformation("FieldList: source={sid} table={t}", sourceId, ctx.TableName);
            var result = await ctx.Engine.FieldListAsync(
                new FieldListRequest { SourceKey = ctx.SourceKey, TableName = ctx.TableName }, ct);

            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Value)
                : _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Schema query failed.");
        }

        /// <summary>
        /// Returns the MAX value of the date field (MaxDateAsync).
        /// </summary>
        [HttpGet("maxdate")]
        public async Task<IActionResult> GetMaxDate(
            [FromQuery] Guid?   sourceId   = null,
            [FromQuery] string? tableName  = null,
            [FromQuery] string  dateField  = "OrderDate",
            CancellationToken   ct         = default)
        {
            using var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
            if (ctx is null) return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            _logger.LogInformation("MaxDate: source={sid} table={t} field={f}", sourceId, ctx.TableName, dateField);
            var result = await ctx.Engine.MaxDateAsync(
                new MaxDateRequest { SourceKey = ctx.SourceKey, TableName = ctx.TableName, DateFieldName = dateField }, ct);

            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(new { maxDate = result.Value!.MaxDate })
                : _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "MaxDate query failed.");
        }

        /// <summary>
        /// Returns up to 200 distinct values for a given column (used for filter LoV picker).
        /// </summary>
        [HttpGet("distinctvalues")]
        public async Task<IActionResult> GetDistinctValues(
            [FromQuery] string  field,
            [FromQuery] Guid?   sourceId  = null,
            [FromQuery] string? tableName = null,
            CancellationToken   ct        = default)
        {
            if (string.IsNullOrWhiteSpace(field))
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "field is required.");

            using var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
            if (ctx is null) return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            var result = await ctx.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = ctx.SourceKey,
                TableName = ctx.TableName,
                ViewBy    = [new GroupField(field)],
                Measures  = [new MeasureField(field, AggregationType.Count, "_cnt")],
            }, ct);

            if (!result.IsSuccess)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Query failed.");

            var values = result.Value!.Rows
                .Select(r => r[field]?.ToString())
                .Where(v => v is not null && v.Length > 0)
                .Order().Take(200).ToList();

            return _responseFactory.CreateOKResponse(new { values });
        }

        /// <summary>GroupBy + Measures aggregation (SummarizeAsync).</summary>
        [HttpPost("summarize")]
        public async Task<IActionResult> Summarize(
            [FromBody] SummarizeRequestDto dto,
            CancellationToken ct = default)
        {
            using var ctx = await TryResolveAsync(dto.Context, ct);
            if (ctx is null) return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            _logger.LogInformation("Summarize: source={sid} table={t}", dto.Context?.SourceId, ctx.TableName);
            var result = await ctx.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = ctx.SourceKey,
                TableName = ctx.TableName,
                ViewBy    = dto.ViewBy.Select(MapGroupField).ToList(),
                Measures  = dto.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias))).ToList(),
                Filters   = dto.Filters?.Select(MapFilter).ToList() ?? [],
            }, ct);

            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(new { rows = result.Value!.Rows.Select(r => r.Data) })
                : _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Summarize failed.");
        }

        /// <summary>Paginated row-level data (DetailsAsync).</summary>
        [HttpPost("details")]
        public async Task<IActionResult> Details(
            [FromBody] DetailsRequestDto dto,
            CancellationToken ct = default)
        {
            using var ctx = await TryResolveAsync(dto.Context, ct);
            if (ctx is null) return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            _logger.LogInformation("Details: source={sid} table={t} skip={s} take={tk}",
                dto.Context?.SourceId, ctx.TableName, dto.Skip, dto.Take);

            var result = await ctx.Engine.DetailsAsync(new DetailsRequest
            {
                SourceKey    = ctx.SourceKey,
                TableName    = ctx.TableName,
                SelectFields = dto.SelectFields ?? [],
                OrderBy      = dto.OrderBy?.Select(o => new SortField(o.FieldName, o.Descending)).ToList() ?? [],
                Skip         = dto.Skip,
                Take         = Math.Min(dto.Take, 200),
                Filters      = dto.Filters?.Select(MapFilter).ToList() ?? [],
            }, ct);

            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(new { rows = result.Value!.Rows, totalCount = result.Value!.TotalCount })
                : _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Details failed.");
        }

        /// <summary>
        /// Natural-language summarize. A two-phase "agent" pipeline:
        /// (1) ask the LLM which string columns the query wants to filter on, (2) fetch the actual
        /// distinct values (List-of-Values) for only those columns, (3) ask the LLM to produce the
        /// final query plan using those real values, then execute it. This stops the LLM from
        /// inventing filter values that don't exist in the data.
        /// Returns <c>{ plan, interpretation, rows }</c>. Anonymous users (demo default source) are not charged.
        /// </summary>
        [HttpPost("summarize/nl")]
        public async Task<IActionResult> NlSummarize(
            [FromBody] NlSummarizeRequestDto dto,
            CancellationToken ct = default)
        {
            var filterPlanPrompt = _promptLoader.GetPrompt("Prompt/DataAnalysis/NlFilterPlanSystem.txt");
            var summarizePrompt  = _promptLoader.GetPrompt("Prompt/DataAnalysis/NlSummarizeSystem.txt");
            if (filterPlanPrompt is null || summarizePrompt is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Prompt configuration error.");

            var userId = CurrentUserId;

            // ── Session data source ────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(dto.SessionId))
            {
                var schemaResult = _sessionStore.GetSchema(dto.SessionId);
                if (!schemaResult.IsSuccess)
                    return _responseFactory.CreateErrorResponse(schemaResult.Error!.Value, schemaResult.ErrorMessage!);

                var sessionFields = (List<SessionFieldDefDto>)((dynamic)schemaResult.Data!).fields;
                var fields = sessionFields.Select(f => (f.Name, f.CanonicalType)).ToList();

                var charge = await TryChargeNlSummarizeAsync(userId);
                if (charge is not null) return charge;

                var planResult = await BuildNlPlanAsync(
                    fields, dto.Query, filterPlanPrompt, summarizePrompt,
                    col => Task.FromResult(GetSessionLov(dto.SessionId!, col)), ct);
                if (planResult.Plan is null)
                    return _responseFactory.CreateErrorResponse(planResult.Error!.Value, planResult.ErrorMessage!);

                var sessionResult = _sessionStore.Summarize(dto.SessionId!, planResult.Plan);
                if (!sessionResult.IsSuccess)
                    return _responseFactory.CreateErrorResponse(sessionResult.Error!.Value, sessionResult.ErrorMessage!);

                var sessionRows = ((IEnumerable<Dictionary<string, object?>>)((dynamic)sessionResult.Data!).rows).Cast<object>();
                _logger.LogInformation("NlSummarize: user={uid} session query={q}", userId ?? "anon", dto.Query);
                return _responseFactory.CreateOKResponse(new NlSummarizeResponseDto
                {
                    Plan           = planResult.Plan,
                    Interpretation = BuildInterpretation(planResult.Plan),
                    Rows           = sessionRows,
                });
            }

            // ── Engine data source (default or custom) ─────────────────────────
            using var ctx = await TryResolveAsync(dto.Context, ct);
            if (ctx is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Source not found.");

            var fieldResult = await ctx.Engine.FieldListAsync(
                new FieldListRequest { SourceKey = ctx.SourceKey, TableName = ctx.TableName }, ct);
            if (!fieldResult.IsSuccess)
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, fieldResult.ErrorMessage ?? "Schema query failed.");

            var engineFields = fieldResult.Value!.Fields.Select(f => (f.Name, f.CanonicalType.ToString())).ToList();

            var engineCharge = await TryChargeNlSummarizeAsync(userId);
            if (engineCharge is not null) return engineCharge;

            var enginePlanResult = await BuildNlPlanAsync(
                engineFields, dto.Query, filterPlanPrompt, summarizePrompt,
                col => GetEngineLovAsync(ctx, col, ct), ct);
            if (enginePlanResult.Plan is null)
                return _responseFactory.CreateErrorResponse(enginePlanResult.Error!.Value, enginePlanResult.ErrorMessage!);

            var plan = enginePlanResult.Plan;
            var result = await ctx.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = ctx.SourceKey,
                TableName = ctx.TableName,
                ViewBy    = plan.ViewBy.Select(MapGroupField).ToList(),
                Measures  = plan.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias))).ToList(),
                Filters   = plan.Filters?.Select(MapFilter).ToList() ?? [],
            }, ct);

            if (!result.IsSuccess)
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.InternalServerError, result.ErrorMessage ?? "Query execution failed.");

            _logger.LogInformation("NlSummarize: user={uid} query={q}", userId ?? "anon", dto.Query);
            return _responseFactory.CreateOKResponse(new NlSummarizeResponseDto
            {
                Plan           = plan,
                Interpretation = BuildInterpretation(plan),
                Rows           = result.Value!.Rows.Select(r => (object)r.Data),
            });
        }

        /// <summary>
        /// The shared two-phase LLM pipeline: plan which columns to filter → fetch their real
        /// values via <paramref name="distinctValuesFetcher"/> → generate &amp; validate the final plan.
        /// Returns the validated plan, or an error code + friendly message on failure.
        /// </summary>
        private async Task<(SummarizeRequestDto? Plan, ErrorCodes? Error, string? ErrorMessage)> BuildNlPlanAsync(
            List<(string Name, string CanonicalType)> fields,
            string query,
            string filterPlanPrompt,
            string summarizePrompt,
            Func<string, Task<List<string>>> distinctValuesFetcher,
            CancellationToken ct)
        {
            if (fields.Count == 0)
                return (null, ErrorCodes.BadRequest, "No fields found in source.");

            var fieldList    = string.Join(", ", fields.Select(f => $"{f.Name}({f.CanonicalType})"));
            var fieldNames   = fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Only String columns are candidates for value-based filtering; numbers/dates use ranges.
            var stringFields = fields
                .Where(f => string.Equals(f.CanonicalType, "String", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ── Phase 1: which string columns does the query want to filter on? ──
            var phase1System = $"{filterPlanPrompt}\n\nAVAILABLE COLUMNS:\n{fieldList}";
            var (filterPlan, _) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<NlFilterPlanDto>(
                phase1System, query, NewModels.GPT_5_NANO);

            var filterColumns = (filterPlan?.FilterColumns ?? [])
                .Where(c => stringFields.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ── Fetch List-of-Values for only the columns we'll filter on ──
            var lovLines = new List<string>();
            foreach (var col in filterColumns)
            {
                ct.ThrowIfCancellationRequested();
                var values = await distinctValuesFetcher(col);
                if (values.Count > 0)
                    lovLines.Add($"- {col}: {string.Join(" | ", values.Take(100))}");
            }
            var lovBlock = lovLines.Count > 0
                ? "\n\nACTUAL VALUES for filterable columns — when filtering on these columns you MUST use exactly these values (match the user's intent to the closest value; if none matches, omit that filter):\n"
                  + string.Join("\n", lovLines)
                : "";

            // ── Phase 2: produce the final query plan using the real values ──
            var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var phase2System = $"{summarizePrompt}\n\nAVAILABLE COLUMNS:\n{fieldList}\nTODAY: {today}{lovBlock}";
            var (plan, llmError) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<SummarizeRequestDto>(
                phase2System, query, NewModels.GPT_5_NANO);

            if (plan is null || plan.ViewBy.Count == 0 || plan.Measures.Count == 0)
            {
                _logger.LogWarning("NlSummarize: empty plan. LLM error={e}", llmError);
                return (null, ErrorCodes.BadRequest, "AI could not interpret your query. Please try rephrasing.");
            }

            // Validate field names against schema (block hallucinated field names)
            var allFieldRefs = plan.ViewBy.Select(v => v.FieldName)
                .Concat(plan.Measures.Select(m => m.FieldName))
                .Concat(plan.Filters?.Select(f => f.FieldName) ?? []);
            var unknownField = allFieldRefs.FirstOrDefault(n => !fieldNames.Contains(n));
            if (unknownField is not null)
            {
                _logger.LogWarning("NlSummarize: LLM hallucinated field '{f}'", unknownField);
                return (null, ErrorCodes.BadRequest, "AI referenced an unknown field. Please try rephrasing.");
            }

            return (plan, null, null);
        }

        /// <summary>Charges NlSummarize credits for an authenticated user; anonymous callers are free. Returns an error result on insufficient credits, else null.</summary>
        private async Task<IActionResult?> TryChargeNlSummarizeAsync(string? userId)
        {
            if (userId is not null &&
                !await _creditService.ConsumeCreditsAsync(userId, CreditCosts.NlSummarize, "nl_summarize", "NL Summarize"))
            {
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.Forbidden, $"Insufficient credits. Required: {CreditCosts.NlSummarize}.");
            }
            return null;
        }

        /// <summary>Returns up to 200 distinct values for a column on the resolved engine (List-of-Values for filter planning).</summary>
        private static async Task<List<string>> GetEngineLovAsync(ResolvedCtx ctx, string field, CancellationToken ct)
        {
            var result = await ctx.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = ctx.SourceKey,
                TableName = ctx.TableName,
                ViewBy    = [new GroupField(field)],
                Measures  = [new MeasureField(field, AggregationType.Count, "_cnt")],
            }, ct);

            if (!result.IsSuccess) return [];

            return result.Value!.Rows
                .Select(r => r[field]?.ToString())
                .Where(v => !string.IsNullOrEmpty(v))
                .Order()
                .Take(200)
                .Select(v => v!)
                .ToList();
        }

        /// <summary>Returns distinct values for a column in session data (List-of-Values for filter planning).</summary>
        private List<string> GetSessionLov(string sessionId, string field)
        {
            var result = _sessionStore.GetDistinctValues(sessionId, field);
            if (!result.IsSuccess) return [];
            return ((IEnumerable<string>)((dynamic)result.Data!).values).ToList();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Maps a <see cref="FilterDimensionDto"/> to a <see cref="FilterDimension"/>, honouring the optional Op.</summary>
        private static FilterDimension MapFilter(FilterDimensionDto f)
        {
            var op = f.Op switch
            {
                "NotIn"       => FilterOperator.NotIn,
                "Equals"      => FilterOperator.Equals,
                "GreaterThan" => FilterOperator.GreaterThan,
                "LessThan"    => FilterOperator.LessThan,
                "Between"     => FilterOperator.Between,
                "Contains"    => FilterOperator.Contains,
                _             => FilterOperator.In,   // default, also handles "In" and null
            };
            return new FilterDimension(f.FieldName, f.Values, op);
        }

        /// <summary>Maps a <see cref="GroupFieldDto"/> to the appropriate <see cref="GroupFieldBase"/> subtype.</summary>
        private static GroupFieldBase MapGroupField(GroupFieldDto f) => f.Type switch
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

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s;

        private static string BuildInterpretation(SummarizeRequestDto plan)
        {
            var viewBy   = string.Join(", ", plan.ViewBy.Select(v => v.Alias ?? v.FieldName));
            var measures = string.Join(", ", plan.Measures.Select(m => $"{m.Aggregation}({m.Alias ?? m.FieldName})"));
            var filters  = plan.Filters is { Count: > 0 }
                ? " | Filters: " + string.Join("; ", plan.Filters.Select(f =>
                    $"{f.FieldName} {f.Op ?? "In"} [{string.Join(", ", f.Values.Take(3))}{(f.Values.Count > 3 ? ", …" : "")}]"))
                : "";
            return $"Grouped by {viewBy}, {measures}{filters}";
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Session upload — temporary in-memory data source (see ISessionDataStore).
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Upload tabular data into a temporary in-memory session (expires in 2h, never persisted).</summary>
        [HttpPost("session")]
        public IActionResult UploadSession([FromBody] SessionUploadDto dto) =>
            ToActionResult(_sessionStore.Upload(dto));

        /// <summary>Returns the schema of a session.</summary>
        [HttpGet("session/{sessionId}/schema")]
        public IActionResult GetSessionSchema(string sessionId) =>
            ToActionResult(_sessionStore.GetSchema(sessionId));

        /// <summary>MAX value of a date column in the session data.</summary>
        [HttpGet("session/{sessionId}/maxdate")]
        public IActionResult GetSessionMaxDate(string sessionId, [FromQuery] string dateField = "Date") =>
            ToActionResult(_sessionStore.GetMaxDate(sessionId, dateField));

        /// <summary>Distinct values for a column in the session data (for filter dropdowns).</summary>
        [HttpGet("session/{sessionId}/distinctvalues")]
        public IActionResult GetSessionDistinctValues(string sessionId, [FromQuery] string field) =>
            ToActionResult(_sessionStore.GetDistinctValues(sessionId, field));

        /// <summary>GroupBy + Measures aggregation on session data.</summary>
        [HttpPost("session/{sessionId}/summarize")]
        public IActionResult SessionSummarize(string sessionId, [FromBody] SummarizeRequestDto dto) =>
            ToActionResult(_sessionStore.Summarize(sessionId, dto));

        /// <summary>Paginated row-level data for a session.</summary>
        [HttpPost("session/{sessionId}/details")]
        public IActionResult SessionDetails(string sessionId, [FromBody] DetailsRequestDto dto) =>
            ToActionResult(_sessionStore.Details(sessionId, dto));

        private IActionResult ToActionResult(ServiceResult result) =>
            result.IsSuccess
                ? _responseFactory.CreateOKResponse(result.Data)
                : _responseFactory.CreateErrorResponse(result.Error!.Value, result.ErrorMessage!);
    }
}
