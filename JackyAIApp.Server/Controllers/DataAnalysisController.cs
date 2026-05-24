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
        ITokenEncryptionService encryptionService) : ControllerBase
    {
        private readonly ILogger<DataAnalysisController> _logger             = logger;
        private readonly IMyResponseFactory              _responseFactory    = responseFactory;
        private readonly DataAnalysisEngine              _defaultEngine      = defaultEngine;
        private readonly AzureSQLDBContext               _dbContext          = dbContext;
        private readonly ITokenEncryptionService         _encryptionService  = encryptionService;

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
                Measures  = dto.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation)).ToList(),
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
    }
}
