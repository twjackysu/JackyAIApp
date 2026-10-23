using DataAnalysisLibrary.Core.Models.Common;
using DataAnalysisLibrary.Core.Models.Requests;
using DataAnalysisLibrary.Core.Models.Results;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;
using static JackyAIApp.Server.Common.DataAnalysisMapping;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The engine a DataAnalysis request queries, the schema gate in front of it, the summarize it runs,
    /// and how a failed query is answered — shared by the query endpoints (<see cref="DataAnalysisQueryService"/>),
    /// the natural-language question (<see cref="NlSummarizeService"/>) and dashboard widgets
    /// (<see cref="DashboardService.GetWidgetDataAsync"/>).
    /// </summary>
    public sealed class EngineSources(
        IDataSourceResolver resolver, ICurrentUser currentUser, IMemoryCache cache, ILogger<EngineSources> logger)
    {
        /// <summary>
        /// The request's source, or why not: NotFound when it doesn't exist or isn't the caller's, BadRequest
        /// when its saved settings are refused (see <see cref="DataSourceConfigException"/>). A custom
        /// source's engine is built for the request and must be disposed after it (<see cref="ResolvedSource"/>).
        /// </summary>
        /// <remarks>A live query resolves the source as the signed-in caller.</remarks>
        public Task<(ResolvedSource? Source, ServiceFailure? Failure)> ResolveAsync(QueryContextDto? context, CancellationToken ct) =>
            ResolveAsync(context, currentUser.Id, ct);

        /// <summary>The source as <paramref name="ownerId"/> has it: a dashboard widget's is its dashboard owner's, whoever views it.</summary>
        public async Task<(ResolvedSource? Source, ServiceFailure? Failure)> ResolveAsync(
            QueryContextDto? context, string? ownerId, CancellationToken ct)
        {
            var (source, status, message) = await resolver.ResolveAsync(context?.SourceId, context?.TableName, ownerId, ct);
            return status switch
            {
                DataSourceResolution.Ok => (source, null),
                DataSourceResolution.ConfigRefused => (null, ServiceResult.Fail(ErrorCodes.BadRequest, message!)),
                _ => (null, ServiceResult.Fail(ErrorCodes.NotFound, "Source not found.")),
            };
        }

        /// <summary>
        /// A failed engine query. The default source is the app's own production database behind anonymous
        /// endpoints, so the driver's text (server and object names, SQL fragments) stays in the log and the
        /// caller gets <paramref name="genericMessage"/>. A custom source is the signed-in owner's own
        /// database: they get the driver's message, which is what they need to fix their connection or query.
        /// </summary>
        /// <remarks>A live query's source is the caller's own (see <see cref="ResolveAsync(QueryContextDto?, CancellationToken)"/>).</remarks>
        public ServiceFailure QueryFailed(ResolvedSource source, string? engineError, string genericMessage) =>
            QueryFailed(source, engineError, genericMessage, viewerIsOwner: true);

        /// <param name="viewerIsOwner">Whether whoever gets the answer owns the source: a dashboard's other
        /// viewers (anonymous ones on an Anyone dashboard included) never get the driver's text.</param>
        public ServiceFailure QueryFailed(ResolvedSource source, string? engineError, string genericMessage, bool viewerIsOwner)
        {
            logger.Log(source.OwnsEngine ? LogLevel.Warning : LogLevel.Error,
                "DataAnalysis query failed: source={src} table={t}: {err}",
                source.OwnsEngine ? "custom" : "default", source.TableName, engineError);
            var message = source.OwnsEngine && viewerIsOwner && !string.IsNullOrWhiteSpace(engineError) ? engineError : genericMessage;
            return ServiceResult.Fail(ErrorCodes.InternalServerError, message);
        }

        /// <summary>Runs a summarize plan on the source; check it with <see cref="GuardAsync"/> first.</summary>
        public Task<QueryResult<SummarizeResult>> SummarizeAsync(ResolvedSource source, SummarizeRequestDto plan, CancellationToken ct) =>
            source.Engine.SummarizeAsync(new SummarizeRequest
            {
                SourceKey = source.SourceKey,
                TableName = source.TableName,
                ViewBy    = plan.ViewBy.Select(MapGroupField).ToList(),
                Measures  = plan.Measures.Select(m => new MeasureField(m.FieldName, m.Aggregation, NullIfEmpty(m.Alias))).ToList(),
                Filters   = MapFilters(plan.Filters),
            }, ct);

        /// <summary>
        /// The schema gate every engine query passes before it runs (see <see cref="DataAnalysisFieldGuard"/>):
        /// BadRequest naming the unknown field or unsafe alias, InternalServerError when the schema can't be
        /// read, or null when the request may go to the engine.
        /// </summary>
        public async Task<ServiceFailure?> GuardAsync(
            ResolvedSource source, Func<IReadOnlySet<string>, string?> check, CancellationToken ct)
        {
            // Only the default source (an engine not built for the request) has a fixed table worth caching.
            var fields = await DataAnalysisFieldGuard.LoadFieldNamesAsync(
                source.Engine, source.SourceKey, source.TableName, source.OwnsEngine ? null : cache, ct);
            if (fields is null)
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Schema query failed.");

            var error = check(fields);
            if (error is null) return null;

            logger.LogWarning("DataAnalysis: rejected query on table={t}: {err}", source.TableName, error);
            return ServiceResult.Fail(ErrorCodes.BadRequest, error);
        }
    }
}
