using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using DataAnalysisLibrary.Core.Models.Results;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using static JackyAIApp.Server.Common.DataAnalysisMapping;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The DataAnalysisLibrary queries behind the Data Analysis page, on the built-in default source or a
    /// signed-in user's custom source (<see cref="QueryContextDto"/>). Every query passes the schema gate
    /// first, and a failure is answered by <see cref="EngineSources.QueryFailed"/>.
    /// </summary>
    public interface IDataAnalysisQueryService
    {
        Task<ServiceResult<FieldListResult>> GetSchemaAsync(QueryContextDto? context, CancellationToken ct);

        Task<ServiceResult<MaxDateDto>> GetMaxDateAsync(QueryContextDto? context, string dateField, CancellationToken ct);

        /// <summary>Up to 200 distinct values of a column (the filter picker), in the column's natural order.</summary>
        Task<ServiceResult<DistinctValuesDto>> GetDistinctValuesAsync(QueryContextDto? context, string field, CancellationToken ct);

        Task<ServiceResult<SummarizeRowsDto>> SummarizeAsync(SummarizeRequestDto dto, CancellationToken ct);

        /// <summary>One page of rows, at most 200.</summary>
        Task<ServiceResult<DetailsPageDto>> DetailsAsync(DetailsRequestDto dto, CancellationToken ct);

        /// <summary>
        /// Every row matching the filters, in the requested order, handed to <paramref name="write"/> as a
        /// CSV — no paging, one query, capped at <see cref="DataAnalysisQueryService.MaxExportRows"/>.
        /// Skip/Take in the request are ignored.
        /// </summary>
        Task<ServiceResult<NoData>> ExportAsync(DetailsRequestDto dto, Func<CsvExport, Task> write, CancellationToken ct);

        /// <summary>Every matching row of an uploaded session as a CSV, under the same cap as <see cref="ExportAsync"/>.</summary>
        Task<ServiceResult<NoData>> ExportSessionAsync(string sessionId, DetailsRequestDto dto, Func<CsvExport, Task> write);
    }

    public sealed class DataAnalysisQueryService(
        EngineSources sources, ISessionDataStore sessions, ILogger<DataAnalysisQueryService> logger) : IDataAnalysisQueryService
    {
        /// <summary>Most rows one CSV export returns; X-Total-Count still reports how many matched.</summary>
        internal const int MaxExportRows = 100_000;

        /// <summary>Most rows one page of details returns.</summary>
        private const int MaxPageRows = 200;

        // A database export holds up to MaxExportRows rows in memory while it is written, so at most
        // two run at once; the default source is anonymous, so this also bounds what callers can load.
        private static readonly SemaphoreSlim ExportGate = new(2, 2);

        public async Task<ServiceResult<FieldListResult>> GetSchemaAsync(QueryContextDto? context, CancellationToken ct)
        {
            var (source, failure) = await sources.ResolveAsync(context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            logger.LogInformation("FieldList: source={sid} table={t}", context?.SourceId, source.TableName);
            var result = await source.Engine.FieldListAsync(
                new FieldListRequest { SourceKey = source.SourceKey, TableName = source.TableName }, ct);

            return result.IsSuccess
                ? ServiceResult.Ok(result.Value!)
                : sources.QueryFailed(source, result.ErrorMessage, "Schema query failed.");
        }

        public async Task<ServiceResult<MaxDateDto>> GetMaxDateAsync(QueryContextDto? context, string dateField, CancellationToken ct)
        {
            var (source, failure) = await sources.ResolveAsync(context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            if (await sources.GuardAsync(source, fields => DataAnalysisFieldGuard.CheckField(fields, dateField), ct) is { } rejected)
                return rejected;

            logger.LogInformation("MaxDate: source={sid} table={t} field={f}", context?.SourceId, source.TableName, dateField);
            var result = await source.Engine.MaxDateAsync(
                new MaxDateRequest { SourceKey = source.SourceKey, TableName = source.TableName, DateFieldName = dateField }, ct);

            return result.IsSuccess
                ? ServiceResult.Ok(new MaxDateDto(result.Value!.MaxDate))
                : sources.QueryFailed(source, result.ErrorMessage, "MaxDate query failed.");
        }

        public async Task<ServiceResult<DistinctValuesDto>> GetDistinctValuesAsync(QueryContextDto? context, string field, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(field))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "field is required.");

            var (source, failure) = await sources.ResolveAsync(context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            if (await sources.GuardAsync(source, fields => DataAnalysisFieldGuard.CheckField(fields, field), ct) is { } rejected)
                return rejected;

            var result = await source.Engine.DistinctValuesAsync(new DistinctValuesRequest
            {
                SourceKey = source.SourceKey, TableName = source.TableName, FieldNames = [field], Limit = 200,
            }, ct);
            // The picker then just offers no values: not worth failing the page over.
            if (!result.IsSuccess)
            {
                logger.LogWarning("GetDistinctValues: field={field} failed: {err}", field, result.ErrorMessage);
                return ServiceResult.Ok(new DistinctValuesDto([]));
            }

            return ServiceResult.Ok(new DistinctValuesDto(
                result.Value!.Fields[field].Values.Where(v => !string.IsNullOrEmpty(v)).ToList()!));
        }

        public async Task<ServiceResult<SummarizeRowsDto>> SummarizeAsync(SummarizeRequestDto dto, CancellationToken ct)
        {
            var (source, failure) = await sources.ResolveAsync(dto.Context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            if (await sources.GuardAsync(source, fields => DataAnalysisFieldGuard.Check(fields, dto), ct) is { } rejected)
                return rejected;

            logger.LogInformation("Summarize: source={sid} table={t}", dto.Context?.SourceId, source.TableName);
            var result = await source.Engine.SummarizeAsync(SummarizeRequestFor(source, dto), ct);

            return result.IsSuccess
                ? ServiceResult.Ok(new SummarizeRowsDto(result.Value!.Rows.Select(r => r.Data)))
                : sources.QueryFailed(source, result.ErrorMessage, "Summarize failed.");
        }

        public async Task<ServiceResult<DetailsPageDto>> DetailsAsync(DetailsRequestDto dto, CancellationToken ct)
        {
            var (source, failure) = await sources.ResolveAsync(dto.Context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            if (await sources.GuardAsync(source, fields => DataAnalysisFieldGuard.Check(fields, dto), ct) is { } rejected)
                return rejected;

            logger.LogInformation("Details: source={sid} table={t} skip={s} take={tk}",
                dto.Context?.SourceId, source.TableName, dto.Skip, dto.Take);
            var result = await source.Engine.DetailsAsync(DetailsRequestFor(source, dto, dto.Skip, Math.Min(dto.Take, MaxPageRows)), ct);

            return result.IsSuccess
                ? ServiceResult.Ok(new DetailsPageDto(result.Value!.Rows, result.Value!.TotalCount))
                : sources.QueryFailed(source, result.ErrorMessage, "Details failed.");
        }

        public async Task<ServiceResult<NoData>> ExportAsync(DetailsRequestDto dto, Func<CsvExport, Task> write, CancellationToken ct)
        {
            if (!await ExportGate.WaitAsync(0, ct))
                return ServiceResult.Fail(ErrorCodes.TooManyRequests, "Another export is running. Please try again shortly.");
            try
            {
                var (source, failure) = await sources.ResolveAsync(dto.Context, ct);
                if (source is null) return failure!.Value;
                using var _ = source;

                if (await sources.GuardAsync(source, fields => DataAnalysisFieldGuard.Check(fields, dto), ct) is { } rejected)
                    return rejected;

                var result = await source.Engine.DetailsAsync(DetailsRequestFor(source, dto, skip: 0, take: MaxExportRows), ct);
                if (!result.IsSuccess)
                    return sources.QueryFailed(source, result.ErrorMessage, "Export failed.");

                var rows = result.Value!.Rows;
                IReadOnlyList<string> headers = rows.Count > 0 ? rows[0].Keys.ToList()
                    : dto.SelectFields is { Count: > 0 } ? dto.SelectFields
                    : await AllFieldsAsync(source, ct);   // nothing matched: the file still gets its header row

                logger.LogInformation("ExportDetails: source={sid} table={t} rows={n} total={total}",
                    dto.Context?.SourceId, source.TableName, rows.Count, result.Value.TotalCount);
                // Written while the gate is held: the rows stay in memory until then.
                await write(new CsvExport(
                    source.TableName, headers, rows.Select(r => headers.Select(h => r.TryGetValue(h, out var v) ? v : null)),
                    rows.Count, result.Value.TotalCount));
                return ServiceResult.Ok();
            }
            finally
            {
                ExportGate.Release();
            }
        }

        public async Task<ServiceResult<NoData>> ExportSessionAsync(string sessionId, DetailsRequestDto dto, Func<CsvExport, Task> write)
        {
            var result = sessions.Export(sessionId, dto, MaxExportRows);
            if (!result.IsSuccess) return result.AsFailure();

            var export = result.Data;
            await write(new CsvExport(
                export.Name, export.Headers,
                export.Rows.Select(r => export.Headers.Select(h => (object?)(r.TryGetValue(h, out var v) ? v : null))),
                export.Rows.Count, export.TotalCount));
            return ServiceResult.Ok();
        }

        /// <summary>The engine request for a summarize plan; the NL question runs its plan the same way.</summary>
        internal static SummarizeRequest SummarizeRequestFor(ResolvedSource source, SummarizeRequestDto dto) => new()
        {
            SourceKey = source.SourceKey,
            TableName = source.TableName,
            ViewBy    = dto.ViewBy.Select(MapGroupField).ToList(),
            Measures  = dto.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias))).ToList(),
            Filters   = MapFilters(dto.Filters),
        };

        private static DetailsRequest DetailsRequestFor(ResolvedSource source, DetailsRequestDto dto, int skip, int take) => new()
        {
            SourceKey    = source.SourceKey,
            TableName    = source.TableName,
            SelectFields = dto.SelectFields ?? [],
            OrderBy      = dto.OrderBy?.Select(o => new SortField(o.FieldName, o.Descending)).ToList() ?? [],
            Skip         = skip,
            Take         = take,
            Filters      = MapFilters(dto.Filters),
        };

        private static async Task<IReadOnlyList<string>> AllFieldsAsync(ResolvedSource source, CancellationToken ct)
        {
            var schema = await source.Engine.FieldListAsync(
                new FieldListRequest { SourceKey = source.SourceKey, TableName = source.TableName }, ct);
            return schema.IsSuccess ? schema.Value!.Fields.Select(f => f.Name).ToList() : [];
        }
    }
}
