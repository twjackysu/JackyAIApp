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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using static JackyAIApp.Server.Common.DataAnalysisMapping;

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
        IUsageLimitService usageLimitService,
        IPromptLoader promptLoader,
        IMemoryCache cache) : ApiControllerBase
    {
        private readonly ILogger<DataAnalysisController> _logger               = logger;
        private readonly IMyResponseFactory              _responseFactory       = responseFactory;
        private readonly DataAnalysisEngine              _defaultEngine         = defaultEngine;
        private readonly AzureSQLDBContext               _dbContext             = dbContext;
        private readonly IDataSourceConfigService        _configService         = configService;
        private readonly ISessionDataStore               _sessionStore          = sessionStore;
        private readonly IOpenAIPromptService            _openAIPromptService   = openAIPromptService;
        private readonly IUsageLimitService              _usageLimitService     = usageLimitService;
        private readonly IPromptLoader                   _promptLoader          = promptLoader;
        private readonly IMemoryCache                    _cache                 = cache;

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
        /// Returns up to 200 distinct values for a given column (used for filter LoV picker), in the
        /// column's natural order.
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

            var result = await ctx.Engine.DistinctValuesAsync(new DistinctValuesRequest
            {
                SourceKey = ctx.SourceKey, TableName = ctx.TableName, FieldNames = [field], Limit = 200,
            }, ct);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("GetDistinctValues: field={field} failed: {err}", field, result.ErrorMessage);
                return _responseFactory.CreateOKResponse(new { values = Array.Empty<string>() });
            }

            var values = result.Value!.Fields[field].Values.Where(v => !string.IsNullOrEmpty(v)).ToList();
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
                Filters   = MapFilters(dto.Filters),
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
                Filters      = MapFilters(dto.Filters),
            }, ct);

            return result.IsSuccess
                ? _responseFactory.CreateOKResponse(new { rows = result.Value!.Rows, totalCount = result.Value!.TotalCount })
                : _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, result.ErrorMessage ?? "Details failed.");
        }

        /// <summary>
        /// Natural-language summarize. A fixed "agent" pipeline: (1) fetch the actual distinct values
        /// (List-of-Values) of the String columns, (2) ask the LLM for the query plan with those real
        /// values in the prompt, (3) validate every filter value against the data — retrying once with
        /// the correct options when one doesn't exist — then execute it. This stops the LLM from
        /// inventing filter values that don't exist in the data.
        /// Returns <c>{ plan, interpretation, rows, notes }</c>. Requires login — every call consumes usage budget.
        /// </summary>
        [HttpPost("summarize/nl")]
        [Authorize]
        public async Task<IActionResult> NlSummarize(
            [FromBody] NlSummarizeRequestDto dto,
            CancellationToken ct = default)
        {
            var summarizePrompt = _promptLoader.GetPrompt("Prompt/DataAnalysis/NlSummarizeSystem.txt");
            if (summarizePrompt is null)
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
                    fields, dto.Query, summarizePrompt,
                    new SessionNlValueSource(_sessionStore, dto.SessionId!),
                    col => Task.FromResult(GetSessionMaxDateValue(dto.SessionId!, col)), ct);
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
                    Interpretation = NlPlanValidator.BuildInterpretation(planResult.Plan),
                    Rows           = sessionRows,
                    Notes          = planResult.Notes.Count > 0 ? planResult.Notes : null,
                    Unmatched      = planResult.Unmatched.Count > 0 ? planResult.Unmatched : null,
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
                engineFields, dto.Query, summarizePrompt,
                new EngineNlValueSource(ctx.Engine, ctx.SourceKey, ctx.TableName,
                    dto.Context?.SourceId?.ToString() ?? "default", _cache, _logger),
                col => FetchEngineMaxDateAsync(ctx, col, ct), ct);
            if (enginePlanResult.Plan is null)
                return _responseFactory.CreateErrorResponse(enginePlanResult.Error!.Value, enginePlanResult.ErrorMessage!);

            var plan = enginePlanResult.Plan;
            var result = await ctx.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = ctx.SourceKey,
                TableName = ctx.TableName,
                ViewBy    = plan.ViewBy.Select(MapGroupField).ToList(),
                Measures  = plan.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias))).ToList(),
                Filters   = MapFilters(plan.Filters),
            }, ct);

            if (!result.IsSuccess)
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.InternalServerError, result.ErrorMessage ?? "Query execution failed.");

            _logger.LogInformation("NlSummarize: user={uid} query={q}", userId ?? "anon", dto.Query);
            return _responseFactory.CreateOKResponse(new NlSummarizeResponseDto
            {
                Plan           = plan,
                Interpretation = NlPlanValidator.BuildInterpretation(plan),
                Rows           = result.Value!.Rows.Select(r => (object)r.Data),
                Notes          = enginePlanResult.Notes.Count > 0 ? enginePlanResult.Notes : null,
                Unmatched      = enginePlanResult.Unmatched.Count > 0 ? enginePlanResult.Unmatched : null,
            });
        }

        /// <summary>
        /// The shared NL pipeline: read every String column's real values from
        /// <paramref name="valueSource"/> → generate the plan with those values in the prompt →
        /// validate it against the data (one self-correcting retry when values had to be removed).
        /// Date columns' max value is fetched via <paramref name="maxDateFetcher"/> and injected so the
        /// LLM can resolve "latest / most recent" requests against the data's real date range.
        /// Returns the validated plan plus any soft notes (a requested filter value that didn't exist and
        /// was dropped, or a condition the LLM reported it couldn't map onto the data) and those unmatched
        /// conditions in the user's wording, or an error code + friendly message on failure.
        /// </summary>
        private async Task<(SummarizeRequestDto? Plan, ErrorCodes? Error, string? ErrorMessage, List<string> Notes, List<string> Unmatched)> BuildNlPlanAsync(
            List<(string Name, string CanonicalType)> fields,
            string query,
            string summarizePrompt,
            INlValueSource valueSource,
            Func<string, Task<string?>> maxDateFetcher,
            CancellationToken ct)
        {
            if (fields.Count == 0)
                return (null, ErrorCodes.BadRequest, "No fields found in source.", [], []);

            var fieldList    = string.Join(", ", fields.Select(f => $"{f.Name}({f.CanonicalType})"));
            var fieldNames   = fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Only String columns are candidates for value-based filtering; numbers/dates use ranges.
            var stringFieldList = fields
                .Where(f => string.Equals(f.CanonicalType, "String", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .ToList();
            var stringFields = stringFieldList.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Date columns — their max value lets the LLM resolve relative dates ("latest", "this month").
            var dateFields = fields
                .Where(f => f.CanonicalType is "DateTime" or "DateTimeOffset" or "Date")
                .Select(f => f.Name)
                .Take(3) // bound the number of extra MAX(date) queries
                .ToList();

            // ── Fetch every String column's real values (one DistinctValues query) ──
            // Read straight from the data, so nothing about a particular dataset is hard-coded in the
            // prompt: whatever codes a column really holds ("LATAM", "Online"…) are what the LLM maps the
            // user's wording onto. Each column returns at most PromptValueLimit values; a column with
            // more (CustomerName…) is marked truncated, left out of the prompt, and validated by lookup
            // instead. Null when the values couldn't be read in time — the question then runs without
            // listed values, and filters are still validated by lookup.
            var columnValues = await valueSource.DistinctAsync(stringFieldList.Take(MaxLovColumns).ToList(), PromptValueLimit, ct)
                               ?? new Dictionary<string, ColumnValues>(StringComparer.OrdinalIgnoreCase);

            var inPrompt    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lovLines    = new List<string>();
            var valueBudget = MaxPromptValues;
            foreach (var col in stringFieldList)
            {
                if (!columnValues.TryGetValue(col, out var cv) ||
                    cv.IsTruncated || cv.Values.Count == 0 || cv.Values.Count > valueBudget)
                    continue;

                lovLines.Add($"- {col}: {string.Join(" | ", cv.Values)}");
                inPrompt.Add(col);
                valueBudget -= cv.Values.Count;
            }
            var lovBlock = lovLines.Count > 0
                ? "\n\nACTUAL VALUES for filterable columns — when filtering on these columns you MUST use exactly these values (match the user's intent to the closest value — synonyms, translations and abbreviations included; if none matches, omit that filter AND put the user's wording in \"unmatched\"):\n"
                  + string.Join("\n", lovLines)
                : "";

            _logger.LogInformation("NlSummarize lovBlock: {lov}", lovBlock.Length > 0 ? lovBlock : "(none)");

            // ── Fetch the latest date per date column (for "latest / most recent" queries) ──
            var dateLines = new List<string>();
            foreach (var col in dateFields)
            {
                ct.ThrowIfCancellationRequested();
                var maxDate = await maxDateFetcher(col);
                if (!string.IsNullOrEmpty(maxDate))
                    dateLines.Add(NlDateRanges.Describe(col, maxDate));
            }
            var dateBlock = dateLines.Count > 0
                ? "\n\nDATA DATE RANGE — the most recent date present in each date column, with the relative ranges "
                  + "already computed from it (NOT from today's date). For \"latest\", \"this month\", \"last quarter\" "
                  + "and the like, copy the matching range verbatim:\n"
                  + string.Join("\n", dateLines)
                : "";

            _logger.LogInformation("NlSummarize Phase2 dateBlock: {db}", dateBlock.Length > 0 ? dateBlock : "(none)");

            // ── Generate the query plan using the real values ──
            var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var phase2System = $"{summarizePrompt}\n\nAVAILABLE COLUMNS:\n{fieldList}\nTODAY: {today}{lovBlock}{dateBlock}";

            var result = await GeneratePlanAsync(query);
            if (result.Plan is null)
                return (null, ErrorCodes.BadRequest, result.ErrorMessage, [], []);

            // ── Self-correction: one retry when validation had to remove values ──
            // Tell the LLM exactly which values don't exist and what the real ones are. Costs an extra
            // call only on failure; if the retry is unusable, the first (already safe) plan stands.
            if (result.Rejected.Count > 0)
            {
                var feedbackLines = new List<string>();
                foreach (var r in result.Rejected)
                {
                    var bad = string.Join(", ", r.Values.Select(v => $"\"{v}\""));
                    if (inPrompt.Contains(r.FieldName))
                    {
                        feedbackLines.Add($"- {r.FieldName}: {bad} — allowed values: {string.Join(" | ", columnValues[r.FieldName].Values)}");
                        continue;
                    }

                    // Too many values to list — offer real values that resemble what the LLM wrote.
                    var candidates = await FindCandidatesAsync(r.FieldName, r.Values);
                    feedbackLines.Add(candidates.Count > 0
                        ? $"- {r.FieldName}: {bad} — closest real values: {string.Join(" | ", candidates)}"
                        : $"- {r.FieldName}: {bad} — no similar value exists in this column");
                }
                var feedback = string.Join("\n", feedbackLines);
                var retryMessage = $"{query}\n\n[CORRECTION] Your previous plan filtered on values that do not exist in the data:\n{feedback}\n"
                    + "Replace each with the allowed value that matches the user's intent (synonym, translation or abbreviation); "
                    + "if none matches, omit that filter and put the user's wording for it in \"unmatched\". "
                    + "Return the complete corrected JSON plan.";

                _logger.LogInformation("NlSummarize: retrying plan generation after rejected values → {fb}", feedback);
                var retry = await GeneratePlanAsync(retryMessage);
                if (retry.Plan is not null)
                    result = retry with
                    {
                        Notes = NlPlanValidator.MergeRetryNotes(
                            result.Rejected, result.Notes, retry.Plan.Filters, retry.Rejected, retry.Notes, retry.Unmatched),
                    };
            }

            var finalNotes = NlPlanValidator.ComposeNotes(result.Notes, result.Unmatched);
            if (finalNotes.Count > 0)
                _logger.LogInformation("NlSummarize: dropped conditions → {notes}", string.Join("; ", finalNotes));

            return (result.Plan, null, null, finalNotes, result.Unmatched);

            // Generates a plan and validates it: field names must exist (else no plan), and every
            // String-column filter value is checked against the real data. The LLM can still invent a
            // value (e.g. on a column too large for the prompt, or ignoring the list); this check
            // removes it deterministically and notes it for the user. A value the LLM saw had no match
            // and left out instead comes back in its own "unmatched" list.
            async Task<(SummarizeRequestDto? Plan, string? ErrorMessage, List<string> Notes, List<(string FieldName, List<string> Values)> Rejected, List<string> Unmatched)>
                GeneratePlanAsync(string userMessage)
            {
                var (nlPlan, llmError) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                    phase2System, userMessage, NewModels.GPT_6_LUNA, feature: "dataanalysis.nl_summarize");

                if (nlPlan?.ViewBy is not { Count: > 0 } || nlPlan.Measures is not { Count: > 0 })
                {
                    _logger.LogWarning("NlSummarize: empty plan. LLM error={e}", llmError);
                    return (null, "AI could not interpret your query. Please try rephrasing.", [], [], []);
                }

                var plan      = nlPlan.ToSummarizeRequest();
                var unmatched = NlPlanValidator.CleanUnmatched(nlPlan.Unmatched);

                // Validate field names against schema (block hallucinated field names)
                var allFieldRefs = plan.ViewBy.Select(v => v.FieldName)
                    .Concat(plan.Measures.Select(m => m.FieldName))
                    .Concat(plan.Filters?.Select(f => f.FieldName) ?? []);
                var unknownField = allFieldRefs.FirstOrDefault(n => !fieldNames.Contains(n));
                if (unknownField is not null)
                {
                    _logger.LogWarning("NlSummarize: LLM hallucinated field '{f}'", unknownField);
                    return (null, "AI referenced an unknown field. Please try rephrasing.", [], [], []);
                }

                if (plan.Filters is not { Count: > 0 })
                    return (plan, null, [], [], unmatched);

                // Fully listed columns are checked against their list. Any other String column (more values
                // than PromptValueLimit, beyond MaxLovColumns, or unreadable up front) is checked by looking
                // up just the plan's values — when all its filters match exact values; a Contains filter
                // can't be proven wrong by a lookup, so such a column stays unvalidated.
                var verifiable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in plan.Filters.Where(f => stringFields.Contains(f.FieldName))
                             .GroupBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase))
                {
                    if (columnValues.TryGetValue(group.Key, out var cv) && !cv.IsTruncated && cv.Values.Count > 0)
                    {
                        verifiable[group.Key] = cv.Values;
                        continue;
                    }
                    if (group.Any(f => f.Op is "Contains" or "GreaterThan" or "LessThan" or "Between"))
                        continue;

                    var matches = await valueSource.LookupAsync(group.Key, group.SelectMany(f => f.Values).Distinct().ToList(), ct);
                    if (matches is not null)
                        verifiable[group.Key] = matches;
                }

                var (validFilters, notes, rejected) = NlPlanValidator.ValidateFilterValues(plan.Filters, verifiable);
                return (plan with { Filters = validFilters }, null, notes, rejected, unmatched);
            }

            // Real values resembling the rejected ones, for a column too large to list: search by each
            // whole value, then by its longest word, and rank what comes back.
            async Task<List<string>> FindCandidatesAsync(string field, List<string> rejectedValues)
            {
                var found = new List<string>();
                foreach (var value in rejectedValues.Take(3))
                {
                    var hits = await valueSource.SearchAsync(field, value.Trim(), MaxRetryCandidates, ct);
                    if (hits.Count == 0)
                    {
                        var word = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).MaxBy(w => w.Length);
                        if (word is { Length: >= 3 } && word != value.Trim())
                            hits = await valueSource.SearchAsync(field, word, MaxRetryCandidates, ct);
                    }
                    found.AddRange(NlPlanValidator.SuggestCandidates(hits, value));
                }
                return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRetryCandidates).ToList();
            }
        }

        /// <summary>Charges NlSummarize usage. The endpoint is <c>[Authorize]</c>d so a user is always present; returns an error result when the daily usage limit is exceeded, else null.</summary>
        private async Task<IActionResult?> TryChargeNlSummarizeAsync(string? userId)
        {
            if (userId is not null &&
                !await _usageLimitService.TryConsumeAsync(userId, UsageCosts.NlSummarize, "nl_summarize"))
            {
                return _responseFactory.CreateErrorResponse(
                    ErrorCodes.UsageLimitExceeded, "Daily usage limit exceeded.");
            }
            return null;
        }

        /// <summary>A String column with at most this many distinct values has them all listed in the prompt.</summary>
        private const int PromptValueLimit = 300;

        /// <summary>Total distinct values listed in the prompt across all columns (keeps wide tables bounded).</summary>
        private const int MaxPromptValues = 2000;

        /// <summary>Candidate values offered in the self-correction retry for a column too large to list.</summary>
        private const int MaxRetryCandidates = 20;

        /// <summary>Bounds the String columns read per NL request on very wide tables.</summary>
        private const int MaxLovColumns = 30;

        /// <summary>Returns the max (latest) value of a date column on the resolved engine, as an ISO string, or null.</summary>
        private async Task<string?> FetchEngineMaxDateAsync(ResolvedCtx ctx, string field, CancellationToken ct)
        {
            var result = await ctx.Engine.MaxDateAsync(
                new MaxDateRequest { SourceKey = ctx.SourceKey, TableName = ctx.TableName, DateFieldName = field }, ct);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("FetchEngineMaxDate: field={field} failed: {err}", field, result.ErrorMessage);
                return null;
            }
            return result.Value!.MaxDate?.ToString("yyyy-MM-dd");
        }

        /// <summary>Returns the max (latest) value of a date column in session data, as an ISO string, or null.</summary>
        private string? GetSessionMaxDateValue(string sessionId, string field)
        {
            var result = _sessionStore.GetMaxDate(sessionId, field);
            if (!result.IsSuccess) return null;
            return (string?)((dynamic)result.Data!).maxDate;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Session upload — temporary in-memory data source (see ISessionDataStore).
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Upload tabular data into a temporary in-memory session (expires in 2h, never persisted).</summary>
        [HttpPost("session")]
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
    }
}
