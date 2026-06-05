using DataAnalysisLibrary.Connectors.Adx;
using DataAnalysisLibrary.Connectors.MsSql;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Enums;
using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

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
        ITokenEncryptionService encryptionService,
        IMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<DataAnalysisController> _logger             = logger;
        private readonly IMyResponseFactory              _responseFactory    = responseFactory;
        private readonly DataAnalysisEngine              _defaultEngine      = defaultEngine;
        private readonly AzureSQLDBContext               _dbContext          = dbContext;
        private readonly ITokenEncryptionService         _encryptionService  = encryptionService;
        private readonly IMemoryCache                    _cache              = memoryCache;

        private const string     SessionCachePrefix = "da_session_";
        private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(2);

        // ── Default source constants ───────────────────────────────────────────
        private const string DefaultMsSqlKey   = "mssql";
        private const string DefaultAdxKey     = "adx";
        private const string DefaultTableName  = "dbo.Orders";

        // ── Auth helper ────────────────────────────────────────────────────────

        private string? GetCurrentUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;


        // ── Engine resolution ──────────────────────────────────────────────────

        private sealed record ResolvedCtx(DataAnalysisEngine Engine, string SourceKey, string TableName);

        private async Task<ResolvedCtx?> TryResolveAsync(
            QueryContextDto? ctx, CancellationToken ct)
        {
            if (ctx?.SourceId is null)
                return new ResolvedCtx(_defaultEngine, DefaultMsSqlKey, ctx?.TableName ?? DefaultTableName);

            var userId = GetCurrentUserId();
            // Custom sources require an authenticated user who owns the record.
            if (userId is null) return null;

            var source = await _dbContext.DataSourceConfigs
                .FirstOrDefaultAsync(s => s.Id == ctx.SourceId.Value && s.UserId == userId, ct);
            if (source is null) return null;

            var engine = BuildEngine(source, _encryptionService.DecryptToken(source.ConfigJson));
            var sourceKey = source.ConnectorType == DataSourceConnectorType.Adx
                ? DefaultAdxKey : DefaultMsSqlKey;
            var tableName = ctx.TableName ?? source.DefaultTableName ?? "";
            return new ResolvedCtx(engine, sourceKey, tableName);
        }

        private static DataAnalysisEngine BuildEngine(DataSourceConfig source, string decryptedConfigJson)
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return source.ConnectorType switch
            {
                DataSourceConnectorType.MsSql => BuildMsSqlEngine(decryptedConfigJson, opts),
                DataSourceConnectorType.Adx   => BuildAdxEngine(decryptedConfigJson, opts),
                _ => throw new NotSupportedException($"Unsupported connector type: {source.ConnectorType}")
            };
        }

        private static DataAnalysisEngine BuildMsSqlEngine(string configJson, JsonSerializerOptions opts)
        {
            var cfg = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(configJson, opts)
                      ?? throw new InvalidOperationException("Invalid MsSql config JSON.");
            return new DataAnalysisEngineBuilder()
                .AddSource(new MsSqlDataSourceFactory(new MsSqlConfig
                {
                    SourceKey        = DefaultMsSqlKey,
                    ConnectionString = cfg.ConnectionString
                }))
                .Build();
        }

        private static DataAnalysisEngine BuildAdxEngine(string configJson, JsonSerializerOptions opts)
        {
            var cfg = JsonSerializer.Deserialize<AdxSourceConfigDto>(configJson, opts)
                      ?? throw new InvalidOperationException("Invalid Adx config JSON.");
            return new DataAnalysisEngineBuilder()
                .AddSource(new AdxDataSourceFactory(new AdxConfig
                {
                    SourceKey        = DefaultAdxKey,
                    ClusterUri       = cfg.ClusterUri,
                    Database         = cfg.Database,
                    TenantId         = cfg.TenantId,
                    ClientId         = cfg.ClientId,
                    ClientSecret     = cfg.ClientSecret,
                    ConnectionString = cfg.ConnectionString
                }))
                .Build();
        }

        private DataSourceConfigResponseDto ToResponseDto(DataSourceConfig s)
        {
            var decryptedJson = _encryptionService.DecryptToken(s.ConfigJson);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            object maskedConfig = s.ConnectorType switch
            {
                DataSourceConnectorType.MsSql => MaskMsSql(decryptedJson, opts),
                DataSourceConnectorType.Adx   => MaskAdx(decryptedJson, opts),
                _                             => new { }
            };
            return new DataSourceConfigResponseDto
            {
                Id               = s.Id,
                Name             = s.Name,
                ConnectorType    = s.ConnectorType,
                Config           = maskedConfig,
                DefaultTableName = s.DefaultTableName,
                CreatedAt        = s.CreatedAt,
            };
        }

        private static object MaskMsSql(string json, JsonSerializerOptions opts)
        {
            var c = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(json, opts);
            if (c is null) return new { };
            // Mask password in connection string but keep Server/Database visible.
            var masked = System.Text.RegularExpressions.Regex.Replace(
                c.ConnectionString, @"(?i)(password|pwd)\s*=\s*[^;]+", "Password=***");
            return new MsSqlSourceConfigDto { ConnectionString = masked };
        }

        private static object MaskAdx(string json, JsonSerializerOptions opts)
        {
            var c = JsonSerializer.Deserialize<AdxSourceConfigDto>(json, opts);
            if (c is null) return new { };
            return new AdxSourceConfigDto
            {
                ClusterUri       = c.ClusterUri,
                Database         = c.Database,
                TenantId         = c.TenantId,
                ClientId         = c.ClientId,
                ClientSecret     = string.IsNullOrEmpty(c.ClientSecret) ? null : "***",
                ConnectionString = string.IsNullOrEmpty(c.ConnectionString) ? null : "***",
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Source CRUD
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>List the current user's saved data-source connectors.</summary>
        [Authorize]
        [HttpGet("sources")]
        public async Task<IActionResult> GetSources(CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var sources = await _dbContext.DataSourceConfigs
                .Where(s => s.UserId == userId)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync(ct);
            return _responseFactory.CreateOKResponse(sources.Select(ToResponseDto).ToList());
        }

        /// <summary>Save a new data-source connector for the current user.</summary>
        [Authorize]
        [HttpPost("sources")]
        public async Task<IActionResult> CreateSource(
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            if (userId is null)
                return _responseFactory.CreateErrorResponse(ErrorCodes.Unauthorized, "Not authenticated.");

            var entity = new DataSourceConfig
            {
                Id               = Guid.NewGuid(),
                UserId           = userId,
                Name             = dto.Name,
                ConnectorType    = dto.ConnectorType,
                ConfigJson       = _encryptionService.EncryptToken(SerializeConfig(dto.Config, dto.ConnectorType)),
                DefaultTableName = dto.DefaultTableName,
                CreatedAt        = DateTimeOffset.UtcNow,
            };
            _dbContext.DataSourceConfigs.Add(entity);
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Created DataSourceConfig {id} ({name}) for user {uid}", entity.Id, entity.Name, userId);
            return _responseFactory.CreateOKResponse(ToResponseDto(entity));
        }

        /// <summary>Update one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpPut("sources/{id:guid}")]
        public async Task<IActionResult> UpdateSource(
            Guid id,
            [FromBody] SaveDataSourceConfigDto dto,
            CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
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
            var newJson            = SerializeConfig(dto.Config, dto.ConnectorType);
            var decryptedExisting  = _encryptionService.DecryptToken(entity.ConfigJson);
            var mergedJson         = MergeConfigSecrets(newJson, decryptedExisting, dto.ConnectorType);
            entity.ConfigJson      = _encryptionService.EncryptToken(mergedJson);

            await _dbContext.SaveChangesAsync(ct);
            return _responseFactory.CreateOKResponse(ToResponseDto(entity));
        }

        /// <summary>Delete one of the current user's data-source connectors.</summary>
        [Authorize]
        [HttpDelete("sources/{id:guid}")]
        public async Task<IActionResult> DeleteSource(Guid id, CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
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

        private static string SerializeConfig(object config, DataSourceConnectorType type)
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            // config may arrive as JsonElement (System.Text.Json model binding)
            if (config is JsonElement je)
            {
                // Re-deserialize into the typed DTO to normalise
                return type switch
                {
                    DataSourceConnectorType.MsSql =>
                        JsonSerializer.Serialize(je.Deserialize<MsSqlSourceConfigDto>(opts), opts),
                    DataSourceConnectorType.Adx =>
                        JsonSerializer.Serialize(je.Deserialize<AdxSourceConfigDto>(opts), opts),
                    _ => je.GetRawText()
                };
            }
            return JsonSerializer.Serialize(config, opts);
        }

        /// <summary>
        /// Merges the incoming (potentially masked) config with the existing stored config.
        /// Non-secret fields are always taken from <paramref name="newJson"/>.
        /// Secret fields ("***" placeholders) fall back to the values in <paramref name="existingJson"/>.
        /// </summary>
        private static string MergeConfigSecrets(string newJson, string existingJson, DataSourceConnectorType type)
        {
            var read  = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var write = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            try
            {
                if (type == DataSourceConnectorType.Adx)
                {
                    var n = JsonSerializer.Deserialize<AdxSourceConfigDto>(newJson,      read);
                    var o = JsonSerializer.Deserialize<AdxSourceConfigDto>(existingJson, read);
                    if (n is null) return existingJson;
                    return JsonSerializer.Serialize(new AdxSourceConfigDto
                    {
                        ClusterUri       = n.ClusterUri,
                        Database         = n.Database,
                        TenantId         = n.TenantId,
                        ClientId         = n.ClientId,
                        // Keep the stored secret when the client echoed back the masked placeholder.
                        ClientSecret     = n.ClientSecret     == "***" ? o?.ClientSecret     : n.ClientSecret,
                        ConnectionString = n.ConnectionString == "***" ? o?.ConnectionString : n.ConnectionString,
                    }, write);
                }
                if (type == DataSourceConnectorType.MsSql)
                {
                    var n = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(newJson,      read);
                    var o = JsonSerializer.Deserialize<MsSqlSourceConfigDto>(existingJson, read);
                    if (n is null) return existingJson;
                    // Restore the original connection string when the password is still masked.
                    var connStr = n.ConnectionString.Contains("Password=***")
                        ? o?.ConnectionString ?? n.ConnectionString
                        : n.ConnectionString;
                    return JsonSerializer.Serialize(new MsSqlSourceConfigDto { ConnectionString = connStr }, write);
                }
            }
            catch { /* fall through */ }
            return newJson;
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
            var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
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
            var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
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

            var ctx = await TryResolveAsync(new QueryContextDto { SourceId = sourceId, TableName = tableName }, ct);
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
            var ctx = await TryResolveAsync(dto.Context, ct);
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
            var ctx = await TryResolveAsync(dto.Context, ct);
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

        // ═══════════════════════════════════════════════════════════════════════
        // Session upload — temporary in-memory data source
        // Data is stored for 2 hours and never persisted to the database.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upload tabular data (all values as strings) into a temporary in-memory session.
        /// Returns a sessionId that can be used with the session query endpoints.
        /// The session expires after 2 hours and is never persisted to disk.
        /// </summary>
        [HttpPost("session")]
        public IActionResult UploadSession([FromBody] SessionUploadDto dto)
        {
            if (dto.Rows is null || dto.Rows.Count == 0)
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "No rows provided.");

            if (dto.Rows.Count > 50_000)
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "Maximum 50,000 rows per session.");

            var schema  = InferSchema(dto.Rows);
            var expires = DateTimeOffset.UtcNow.Add(SessionTtl);
            var id      = Guid.NewGuid().ToString("N");

            _cache.Set(SessionCachePrefix + id, new SessionData
            {
                Name      = dto.Name?.Trim() is { Length: > 0 } n ? n : "Uploaded Data",
                Rows      = dto.Rows,
                Schema    = schema,
                ExpiresAt = expires,
            }, SessionTtl);

            _logger.LogInformation("Session {id} created with {n} rows", id, dto.Rows.Count);

            return _responseFactory.CreateOKResponse(new SessionUploadResponseDto
            {
                SessionId = id,
                Name      = dto.Name?.Trim() is { Length: > 0 } n2 ? n2 : "Uploaded Data",
                RowCount  = dto.Rows.Count,
                ExpiresAt = expires,
                Schema    = schema,
            });
        }

        /// <summary>Returns the schema of a session (same shape as /schema for regular sources).</summary>
        [HttpGet("session/{sessionId}/schema")]
        public IActionResult GetSessionSchema(string sessionId)
        {
            if (!TryGetSession(sessionId, out var session))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Session not found or expired.");

            return _responseFactory.CreateOKResponse(new { fields = session!.Schema });
        }

        /// <summary>MAX value of a date column in the session data.</summary>
        [HttpGet("session/{sessionId}/maxdate")]
        public IActionResult GetSessionMaxDate(string sessionId, [FromQuery] string dateField = "Date")
        {
            if (!TryGetSession(sessionId, out var session))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Session not found or expired.");

            var values = session!.Rows
                .Select(r => r.GetValueOrDefault(dateField))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => TryParseDate(v!) ? DateTimeOffset.Parse(v!, CultureInfo.InvariantCulture) : (DateTimeOffset?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            var maxDate = values.Count > 0 ? values.Max().ToString("o") : null;
            return _responseFactory.CreateOKResponse(new { maxDate });
        }

        /// <summary>Distinct values for a column in the session data (for filter dropdowns).</summary>
        [HttpGet("session/{sessionId}/distinctvalues")]
        public IActionResult GetSessionDistinctValues(string sessionId, [FromQuery] string field)
        {
            if (!TryGetSession(sessionId, out var session))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Session not found or expired.");

            var values = session!.Rows
                .Select(r => r.GetValueOrDefault(field))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .Take(200)
                .ToList();

            return _responseFactory.CreateOKResponse(new { values });
        }

        /// <summary>GroupBy + Measures aggregation on session data. Uses the same request shape as /summarize.</summary>
        [HttpPost("session/{sessionId}/summarize")]
        public IActionResult SessionSummarize(string sessionId, [FromBody] SummarizeRequestDto dto)
        {
            if (!TryGetSession(sessionId, out var session))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Session not found or expired.");

            var schemaLookup = session!.Schema.ToDictionary(s => s.Name, s => s.CanonicalType, StringComparer.OrdinalIgnoreCase);

            var filtered = session.Rows.Where(row =>
                (dto.Filters ?? []).All(f => SessionMatchesFilter(row, f))).ToList();

            IEnumerable<IGrouping<string, Dictionary<string, string>>> groups;
            if (dto.ViewBy.Count == 0)
            {
                // No group-by: treat all rows as a single group
                groups = filtered.GroupBy(_ => "");
            }
            else
            {
                groups = filtered.GroupBy(row =>
                    string.Join("\0", dto.ViewBy.Select(vb =>
                        SessionGetGroupKey(row, vb, schemaLookup))));
            }

            var rows = groups.Select(g =>
            {
                var dict = new Dictionary<string, object?>();

                // Group key fields
                var keyParts = g.Key.Split('\0');
                for (var i = 0; i < dto.ViewBy.Count; i++)
                {
                    var vb    = dto.ViewBy[i];
                    var alias = NullIfEmpty(vb.Alias) ?? vb.FieldName;
                    dict[alias] = i < keyParts.Length ? keyParts[i] : null;
                }

                // Aggregations
                foreach (var measure in dto.Measures)
                {
                    var alias        = NullIfEmpty(measure.Alias) ?? measure.FieldName;
                    var canonType    = schemaLookup.GetValueOrDefault(measure.FieldName, "String");
                    var cellValues   = g.Select(row => row.GetValueOrDefault(measure.FieldName));
                    dict[alias]      = SessionAggregate(cellValues, measure.Aggregation, canonType);
                }

                return dict;
            }).ToList();

            return _responseFactory.CreateOKResponse(new { rows });
        }

        /// <summary>Paginated row-level data for session. Uses the same request shape as /details.</summary>
        [HttpPost("session/{sessionId}/details")]
        public IActionResult SessionDetails(string sessionId, [FromBody] DetailsRequestDto dto)
        {
            if (!TryGetSession(sessionId, out var session))
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "Session not found or expired.");

            var filtered = session!.Rows
                .Where(row => (dto.Filters ?? []).All(f => SessionMatchesFilter(row, f)))
                .ToList();

            // Sorting
            var schemaLookup = session.Schema.ToDictionary(s => s.Name, s => s.CanonicalType, StringComparer.OrdinalIgnoreCase);
            if (dto.OrderBy is { Count: > 0 })
            {
                var sort = dto.OrderBy[0];
                filtered = sort.Descending
                    ? filtered.OrderByDescending(r => SessionSortKey(r, sort.FieldName, schemaLookup)).ToList()
                    : filtered.OrderBy(r => SessionSortKey(r, sort.FieldName, schemaLookup)).ToList();
            }

            var totalCount = filtered.Count;
            var skip       = dto.Skip;
            var take       = Math.Min(dto.Take, 200);
            var page       = filtered.Skip(skip).Take(take).ToList();

            // Convert to Record<string, object?> to match regular details shape
            var rows = page.Select(r => r.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value)).ToList();

            return _responseFactory.CreateOKResponse(new { rows, totalCount });
        }

        // ── Session helpers ───────────────────────────────────────────────────

        private bool TryGetSession(string sessionId, out SessionData? session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(sessionId)) return false;
            return _cache.TryGetValue(SessionCachePrefix + sessionId, out session) && session is not null;
        }

        private static List<SessionFieldDefDto> InferSchema(List<Dictionary<string, string>> rows)
        {
            if (rows.Count == 0) return [];
            var columns = rows[0].Keys.ToList();
            var sample  = rows.Take(200).ToList();

            return columns.Select(col =>
            {
                var values = sample
                    .Select(r => r.GetValueOrDefault(col))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                var canonType = InferColumnType(values!);
                var dataType  = canonType switch
                {
                    "DateTimeOffset" or "DateTime" => "datetime",
                    "Integer"                      => "bigint",
                    "Decimal"                      => "decimal",
                    "Boolean"                      => "bit",
                    _                              => "nvarchar"
                };
                return new SessionFieldDefDto
                {
                    Name          = col,
                    DataType      = dataType,
                    IsNullable    = true,
                    CanonicalType = canonType,
                };
            }).ToList();
        }

        private static string InferColumnType(List<string> values)
        {
            if (values.Count == 0) return "String";

            // DateTime: must look like a date (min 8 chars) and parse successfully
            if (values.All(v => v.Length >= 6 && TryParseDate(v)))
                return "DateTimeOffset";

            // Integer
            if (values.All(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                return "Integer";

            // Decimal (allow thousand separators)
            if (values.All(v => decimal.TryParse(v.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
                return "Decimal";

            // Boolean
            var bools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "1", "0" };
            if (values.All(v => bools.Contains(v)))
                return "Boolean";

            return "String";
        }

        private static bool TryParseDate(string v) =>
            DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
            DateTimeOffset.TryParse(v, CultureInfo.GetCultureInfo("zh-TW"), DateTimeStyles.None, out _);

        private static string SessionGetGroupKey(
            Dictionary<string, string> row,
            GroupFieldDto field,
            Dictionary<string, string> schemaLookup)
        {
            var raw = row.GetValueOrDefault(field.FieldName);
            if (raw is null) return "(null)";

            if (field.Type == "DateTrunc")
            {
                var canonType = schemaLookup.GetValueOrDefault(field.FieldName, "String");
                if (canonType is "DateTimeOffset" or "DateTime" && TryParseDate(raw) &&
                    DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    return TruncateDate(dt, field.Granularity ?? "Day");
                }
            }

            return raw;
        }

        private static string TruncateDate(DateTimeOffset dt, string granularity) => granularity switch
        {
            "Year"    => $"{dt.Year}",
            "Quarter" => $"{dt.Year}-Q{((dt.Month - 1) / 3) + 1}",
            "Month"   => dt.ToString("yyyy-MM"),
            "Week"    => $"{dt.Year}-W{ISOWeek.GetWeekOfYear(dt.DateTime):D2}",
            "Hour"    => dt.ToString("yyyy-MM-dd HH:00"),
            _         => dt.ToString("yyyy-MM-dd")  // Day
        };

        private static bool SessionMatchesFilter(Dictionary<string, string> row, FilterDimensionDto filter)
        {
            var raw = row.GetValueOrDefault(filter.FieldName) ?? "";
            return filter.Op switch
            {
                "NotIn"       => !filter.Values.Contains(raw, StringComparer.OrdinalIgnoreCase),
                "Equals"      => filter.Values.Count > 0 && raw.Equals(filter.Values[0], StringComparison.OrdinalIgnoreCase),
                "Contains"    => filter.Values.Any(v => raw.Contains(v, StringComparison.OrdinalIgnoreCase)),
                "GreaterThan" => filter.Values.Count > 0 && SessionCompare(raw, filter.Values[0]) > 0,
                "LessThan"    => filter.Values.Count > 0 && SessionCompare(raw, filter.Values[0]) < 0,
                "Between"     => filter.Values.Count >= 2 &&
                                 SessionCompare(raw, filter.Values[0]) >= 0 &&
                                 SessionCompare(raw, filter.Values[1]) <= 0,
                _             => filter.Values.Contains(raw, StringComparer.OrdinalIgnoreCase)  // "In"
            };
        }

        private static int SessionCompare(string a, string b)
        {
            if (decimal.TryParse(a.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var da) &&
                decimal.TryParse(b.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
                return da.CompareTo(db);

            if (DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dta) &&
                DateTimeOffset.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtb))
                return dta.CompareTo(dtb);

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static object? SessionAggregate(
            IEnumerable<string?> rawValues,
            AggregationType aggregation,
            string canonicalType)
        {
            var values = rawValues.ToList();

            if (aggregation == AggregationType.Count)
                return (long)values.Count;

            if (aggregation == AggregationType.DistinctCount)
                return (long)values
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

            // Numeric aggregations
            if (aggregation is AggregationType.Sum or AggregationType.Average)
            {
                var nums = values
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => decimal.TryParse(v!.Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToList();

                if (nums.Count == 0) return null;
                return aggregation == AggregationType.Sum
                    ? nums.Sum()
                    : Math.Round(nums.Average(), 6);
            }

            // Max / Min — type-aware comparison
            var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).ToList();
            if (nonEmpty.Count == 0) return null;

            if (canonicalType is "DateTimeOffset" or "DateTime")
            {
                var dates = nonEmpty
                    .Select(v => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTimeOffset?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (dates.Count == 0) return null;
                return aggregation == AggregationType.Max
                    ? dates.Max().ToString("yyyy-MM-dd")
                    : dates.Min().ToString("yyyy-MM-dd");
            }

            if (canonicalType is "Integer" or "Decimal")
            {
                var nums2 = nonEmpty
                    .Select(v => decimal.TryParse(v!.Replace(",", ""), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                if (nums2.Count == 0) return null;
                return aggregation == AggregationType.Max ? nums2.Max() : nums2.Min();
            }

            return aggregation == AggregationType.Max
                ? nonEmpty.Max(StringComparer.OrdinalIgnoreCase)
                : nonEmpty.Min(StringComparer.OrdinalIgnoreCase);
        }

        private static IComparable SessionSortKey(
            Dictionary<string, string> row,
            string fieldName,
            Dictionary<string, string> schemaLookup)
        {
            var raw       = row.GetValueOrDefault(fieldName) ?? "";
            var canonType = schemaLookup.GetValueOrDefault(fieldName, "String");

            if (canonType is "DateTimeOffset" or "DateTime" &&
                DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            if (canonType is "Integer" or "Decimal" &&
                decimal.TryParse(raw.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;

            return raw;
        }
    }
}
