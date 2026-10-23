using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>The validated plan for a natural-language query, plus any soft notes.</summary>
    /// <param name="Plan">The validated summarize plan, or null when the query could not be planned.</param>
    /// <param name="Error">The error code when <paramref name="Plan"/> is null.</param>
    /// <param name="ErrorMessage">The friendly message when <paramref name="Plan"/> is null.</param>
    /// <param name="Notes">Soft notes: a requested filter value that didn't exist and was dropped.</param>
    /// <param name="Unmatched">Conditions the LLM reported it couldn't map onto the data, in the user's wording.</param>
    public sealed record NlPlanResult(
        SummarizeRequestDto? Plan,
        ErrorCodes? Error,
        string? ErrorMessage,
        List<string> Notes,
        List<string> Unmatched);

    /// <summary>Turns a natural-language question into a validated summarize plan (the LLM pipeline
    /// behind <c>POST api/dataanalysis/summarize/nl</c>), independent of the data source the plan
    /// then runs against (default engine, a user's custom engine, or an uploaded session).</summary>
    public interface INlQueryPlanner
    {
        /// <summary>
        /// Reads every String column's real values from <paramref name="valueSource"/> → generates
        /// the plan with those values in the prompt → validates it against the data (one
        /// self-correcting retry when values had to be removed). Date columns' max value is fetched
        /// via <paramref name="maxDateFetcher"/> and injected so the LLM can resolve "latest / most
        /// recent" against the data's real date range.
        /// </summary>
        Task<NlPlanResult> BuildPlanAsync(
            List<(string Name, string CanonicalType)> fields,
            string query,
            string summarizePrompt,
            INlValueSource valueSource,
            Func<string, Task<string?>> maxDateFetcher,
            CancellationToken ct);
    }

    public class NlQueryPlanner(
        ILogger<NlQueryPlanner> logger,
        IOpenAIPromptService openAIPromptService,
        TimeProvider? timeProvider = null) : INlQueryPlanner
    {
        private readonly ILogger<NlQueryPlanner> _logger = logger;
        private readonly IOpenAIPromptService _openAIPromptService = openAIPromptService;
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        /// <summary>Bounds the String columns read per NL request on very wide tables.</summary>
        private const int MaxLovColumns = 30;

        public async Task<NlPlanResult> BuildPlanAsync(
            List<(string Name, string CanonicalType)> fields,
            string query,
            string summarizePrompt,
            INlValueSource valueSource,
            Func<string, Task<string?>> maxDateFetcher,
            CancellationToken ct)
        {
            if (fields.Count == 0)
                return new NlPlanResult(null, ErrorCodes.BadRequest, "No fields found in source.", [], []);

            var schema = NlSchema.From(fields);
            // One DistinctValues query for every String column. Null when the values couldn't be read in
            // time — the question then runs without listed values, and filters are still checked by lookup.
            var values = NlPromptValues.Select(schema.StringColumns, await valueSource.DistinctAsync(
                schema.StringColumns.Take(MaxLovColumns).ToList(), NlPromptValues.PerColumnLimit, ct));
            var valuesBlock = NlPrompt.ValuesBlock(values.Lines);
            _logger.LogInformation("NlSummarize lovBlock: {lov}", valuesBlock.Length > 0 ? valuesBlock : "(none)");
            var datesBlock = NlPrompt.DatesBlock(await LatestDatesAsync(schema.DateColumns, maxDateFetcher, ct));
            _logger.LogInformation("NlSummarize Phase2 dateBlock: {db}", datesBlock.Length > 0 ? datesBlock : "(none)");

            var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
            var run = new NlPlanRun(_openAIPromptService, _logger, schema, values,
                NlPrompt.System(summarizePrompt, schema.FieldList, today, valuesBlock, datesBlock), valueSource, ct);

            var result = await run.GenerateAsync(query);
            if (result.Plan is null)
                return new NlPlanResult(null, ErrorCodes.BadRequest, result.ErrorMessage, [], []);
            if (result.Rejected.Count > 0)
                result = await run.CorrectAsync(query, result);

            var notes = NlPlanValidator.ComposeNotes(result.Notes, result.Unmatched);
            if (notes.Count > 0)
                _logger.LogInformation("NlSummarize: dropped conditions → {notes}", string.Join("; ", notes));
            return new NlPlanResult(result.Plan, null, null, notes, result.Unmatched);
        }

        /// <summary>Each date column's latest date, for "latest / most recent" questions (one query each).</summary>
        private static async Task<List<string>> LatestDatesAsync(
            IReadOnlyList<string> dateColumns, Func<string, Task<string?>> maxDateFetcher, CancellationToken ct)
        {
            var lines = new List<string>();
            foreach (var column in dateColumns)
            {
                ct.ThrowIfCancellationRequested();
                var maxDate = await maxDateFetcher(column);
                if (!string.IsNullOrEmpty(maxDate))
                    lines.Add(NlDateRanges.Describe(column, maxDate));
            }
            return lines;
        }
    }
}
