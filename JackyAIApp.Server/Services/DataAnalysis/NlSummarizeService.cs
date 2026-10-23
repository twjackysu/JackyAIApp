using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// A question about the data in plain language, answered. A fixed pipeline: the plan comes from
    /// <see cref="INlQueryPlanner"/> (which reads the String columns' real values first and checks every
    /// filter value against the data), then runs on an uploaded session or on the engine — where it
    /// passes the same schema gate as a hand-built query.
    /// </summary>
    public interface INlSummarizeService
    {
        Task<ServiceResult<NlSummarizeResponseDto>> SummarizeAsync(NlSummarizeRequestDto dto, CancellationToken ct);
    }

    public sealed class NlSummarizeService(
        IPromptLoader prompts,
        INlQueryPlanner planner,
        ISessionDataStore sessions,
        EngineSources sources,
        IMemoryCache cache,
        ICurrentUser currentUser,
        ILogger<NlSummarizeService> logger) : INlSummarizeService
    {
        private const string PromptPath = "Prompt/DataAnalysis/NlSummarizeSystem.txt";

        public async Task<ServiceResult<NlSummarizeResponseDto>> SummarizeAsync(NlSummarizeRequestDto dto, CancellationToken ct)
        {
            var rules = prompts.GetPrompt(PromptPath);
            if (rules is null)
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Prompt configuration error.");

            return string.IsNullOrWhiteSpace(dto.SessionId)
                ? await OnEngineAsync(dto, rules, ct)
                : await OnSessionAsync(dto.SessionId, dto.Query, rules, ct);
        }

        private async Task<ServiceResult<NlSummarizeResponseDto>> OnSessionAsync(
            string sessionId, string query, string rules, CancellationToken ct)
        {
            var schema = sessions.GetSchema(sessionId);
            if (!schema.IsSuccess) return schema.AsFailure();

            var planned = await planner.BuildPlanAsync(
                schema.Data.Fields.Select(f => (f.Name, f.CanonicalType)).ToList(), query, rules,
                new SessionNlValueSource(sessions, sessionId),
                column => Task.FromResult(SessionMaxDate(sessionId, column)), ct);
            if (planned.Plan is null)
                return ServiceResult.Fail(planned.Error!.Value, planned.ErrorMessage!);

            var result = sessions.Summarize(sessionId, planned.Plan);
            if (!result.IsSuccess) return result.AsFailure();

            logger.LogInformation("NlSummarize: user={uid} session query={q}", currentUser.Id ?? "anon", query);
            return ServiceResult.Ok(Answer(planned.Plan, result.Data.Rows.Cast<object>(), planned));
        }

        private async Task<ServiceResult<NlSummarizeResponseDto>> OnEngineAsync(
            NlSummarizeRequestDto dto, string rules, CancellationToken ct)
        {
            var (source, failure) = await sources.ResolveAsync(dto.Context, ct);
            if (source is null) return failure!.Value;
            using var _ = source;

            var schema = await source.Engine.FieldListAsync(
                new FieldListRequest { SourceKey = source.SourceKey, TableName = source.TableName }, ct);
            if (!schema.IsSuccess)
                return sources.QueryFailed(source, schema.ErrorMessage, "Schema query failed.");

            var fields = schema.Value!.Fields.Select(f => (f.Name, f.CanonicalType.ToString())).ToList();
            var planned = await planner.BuildPlanAsync(
                fields, dto.Query, rules,
                new EngineNlValueSource(source.Engine, source.SourceKey, source.TableName,
                    dto.Context?.SourceId?.ToString() ?? "default", cache, logger),
                column => EngineMaxDateAsync(source, column, ct), ct);
            if (planned.Plan is null)
                return ServiceResult.Fail(planned.Error!.Value, planned.ErrorMessage!);

            // The same schema gate as the hand-built queries. The LLM's field names were already checked
            // while the plan was made; an alias it wrote that isn't plain text is dropped, not fatal.
            var plan = DataAnalysisFieldGuard.WithoutUnsafeAliases(planned.Plan);
            if (DataAnalysisFieldGuard.Check(fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), plan) is { } planError)
            {
                logger.LogWarning("NlSummarize: rejected plan on table={t}: {err}", source.TableName, planError);
                return ServiceResult.Fail(ErrorCodes.BadRequest, planError);
            }

            var result = await sources.SummarizeAsync(source, plan, ct);
            if (!result.IsSuccess)
                return sources.QueryFailed(source, result.ErrorMessage, "Query execution failed.");

            logger.LogInformation("NlSummarize: user={uid} query={q}", currentUser.Id ?? "anon", dto.Query);
            return ServiceResult.Ok(Answer(plan, result.Value!.Rows.Select(r => (object)r.Data), planned));
        }

        /// <summary>The answer: the plan that ran, what the AI understood, its rows, and the notes only when there are any.</summary>
        private static NlSummarizeResponseDto Answer(SummarizeRequestDto plan, IEnumerable<object> rows, NlPlanResult planned) => new()
        {
            Plan           = plan,
            Interpretation = NlPlanValidator.BuildInterpretation(plan),
            Rows           = rows,
            Notes          = planned.Notes.Count > 0 ? planned.Notes : null,
            Unmatched      = planned.Unmatched.Count > 0 ? planned.Unmatched : null,
        };

        /// <summary>The latest value of a date column on the engine, as yyyy-MM-dd, or null.</summary>
        private async Task<string?> EngineMaxDateAsync(ResolvedSource source, string field, CancellationToken ct)
        {
            var result = await source.Engine.MaxDateAsync(
                new MaxDateRequest { SourceKey = source.SourceKey, TableName = source.TableName, DateFieldName = field }, ct);
            if (!result.IsSuccess)
            {
                logger.LogWarning("FetchEngineMaxDate: field={field} failed: {err}", field, result.ErrorMessage);
                return null;
            }
            return result.Value!.MaxDate?.ToString("yyyy-MM-dd");
        }

        /// <summary>The latest value of a date column in a session, as ISO 8601, or null.</summary>
        private string? SessionMaxDate(string sessionId, string field)
        {
            var result = sessions.GetMaxDate(sessionId, field);
            return result.IsSuccess ? result.Data.MaxDate : null;
        }
    }
}
