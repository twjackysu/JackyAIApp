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
        IOpenAIPromptService openAIPromptService) : INlQueryPlanner
    {
        private readonly ILogger<NlQueryPlanner> _logger = logger;
        private readonly IOpenAIPromptService _openAIPromptService = openAIPromptService;

        /// <summary>A String column with at most this many distinct values has them all listed in the prompt.</summary>
        private const int PromptValueLimit = 300;

        /// <summary>Total distinct values listed in the prompt across all columns (keeps wide tables bounded).</summary>
        private const int MaxPromptValues = 2000;

        /// <summary>Candidate values offered in the self-correction retry for a column too large to list.</summary>
        private const int MaxRetryCandidates = 20;

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
                return new NlPlanResult(null, ErrorCodes.BadRequest, result.ErrorMessage, [], []);

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

            return new NlPlanResult(result.Plan, null, null, finalNotes, result.Unmatched);

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
    }
}
