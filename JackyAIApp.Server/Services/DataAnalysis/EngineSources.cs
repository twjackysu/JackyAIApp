using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The engine a DataAnalysis request queries, the schema gate in front of it, and how a failed query
    /// is answered — shared by the query endpoints (<see cref="DataAnalysisQueryService"/>) and the
    /// natural-language question (<see cref="NlSummarizeService"/>).
    /// </summary>
    public sealed class EngineSources(
        IDataSourceResolver resolver, ICurrentUser currentUser, IMemoryCache cache, ILogger<EngineSources> logger)
    {
        /// <summary>
        /// The request's source, or why not: NotFound when it doesn't exist or isn't the caller's, BadRequest
        /// when its saved settings are refused (see <see cref="DataSourceConfigException"/>). A custom
        /// source's engine is built for the request and must be disposed after it (<see cref="ResolvedSource"/>).
        /// </summary>
        public async Task<(ResolvedSource? Source, ServiceFailure? Failure)> ResolveAsync(QueryContextDto? context, CancellationToken ct)
        {
            // A live query resolves the source as the signed-in caller (the record's owner).
            var (source, status, message) = await resolver.ResolveAsync(context?.SourceId, context?.TableName, currentUser.Id, ct);
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
        public ServiceFailure QueryFailed(ResolvedSource source, string? engineError, string genericMessage)
        {
            logger.Log(source.OwnsEngine ? LogLevel.Warning : LogLevel.Error,
                "DataAnalysis query failed: source={src} table={t}: {err}",
                source.OwnsEngine ? "custom" : "default", source.TableName, engineError);
            var message = source.OwnsEngine && !string.IsNullOrWhiteSpace(engineError) ? engineError : genericMessage;
            return ServiceResult.Fail(ErrorCodes.InternalServerError, message);
        }

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
