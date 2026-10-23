using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>One plan from the LLM, checked against the data.</summary>
    /// <param name="Plan">Null when the plan was unusable (<paramref name="ErrorMessage"/> says why).</param>
    /// <param name="ErrorMessage">Why there is no plan, for the user.</param>
    /// <param name="Notes">One per filter that lost values the data does not have.</param>
    /// <param name="Rejected">Those values, per filter (<paramref name="Notes"/>[i] describes [i]): what the correction names.</param>
    /// <param name="Unmatched">Conditions the LLM said it could not map onto the data, in the user's wording.</param>
    internal sealed record PlanAttempt(
        SummarizeRequestDto? Plan,
        string? ErrorMessage,
        List<string> Notes,
        List<(string FieldName, List<string> Values)> Rejected,
        List<string> Unmatched)
    {
        public static PlanAttempt Unusable(string message) => new(null, message, [], [], []);
    }

    /// <summary>
    /// The LLM calls of one question: a plan, checked against the data, and when it filtered on values
    /// the data does not have, one correction that tells the LLM what the real values are. A new one
    /// for each question.
    /// </summary>
    internal sealed class NlPlanRun(
        IOpenAIPromptService openAI,
        ILogger logger,
        NlSchema schema,
        NlPromptValues values,
        string systemPrompt,
        INlValueSource source,
        CancellationToken ct)
    {
        /// <summary>Candidate values offered in the correction for a column too large to list.</summary>
        internal const int MaxRetryCandidates = 20;

        /// <summary>At most this many of a filter's rejected values are searched for candidates.</summary>
        private const int MaxValuesSearched = 3;

        /// <summary>
        /// Generates a plan and checks it: field names must exist (else no plan), and every String-column
        /// filter value is checked against the real data. The LLM can still invent a value (e.g. on a
        /// column too large for the prompt, or ignoring the list); this check removes it
        /// deterministically and notes it for the user. A value the LLM saw had no match and left out
        /// instead comes back in its own "unmatched" list.
        /// </summary>
        public async Task<PlanAttempt> GenerateAsync(string userMessage)
        {
            var (nlPlan, llmError) = await openAI.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                systemPrompt, userMessage, NewModels.GPT_6_LUNA, feature: "dataanalysis.nl_summarize");

            if (nlPlan?.ViewBy is not { Count: > 0 } || nlPlan.Measures is not { Count: > 0 })
            {
                logger.LogWarning("NlSummarize: empty plan. LLM error={e}", llmError);
                return PlanAttempt.Unusable("AI could not interpret your query. Please try rephrasing.");
            }

            var plan = nlPlan.ToSummarizeRequest();
            var unmatched = NlPlanValidator.CleanUnmatched(nlPlan.Unmatched);

            // Validate field names against schema (block hallucinated field names)
            var unknownField = plan.ViewBy.Select(v => v.FieldName)
                .Concat(plan.Measures.Select(m => m.FieldName))
                .Concat(plan.Filters?.Select(f => f.FieldName) ?? [])
                .FirstOrDefault(n => !schema.Has(n));
            if (unknownField is not null)
            {
                logger.LogWarning("NlSummarize: LLM hallucinated field '{f}'", unknownField);
                return PlanAttempt.Unusable("AI referenced an unknown field. Please try rephrasing.");
            }

            if (plan.Filters is not { Count: > 0 })
                return new PlanAttempt(plan, null, [], [], unmatched);

            var (validFilters, notes, rejected) = NlPlanValidator.ValidateFilterValues(plan.Filters, await CheckableValuesAsync(plan.Filters));
            return new PlanAttempt(plan with { Filters = validFilters }, null, notes, rejected, unmatched);
        }

        /// <summary>
        /// The one correction of a plan that filtered on values the data does not have: the LLM is told
        /// exactly which values don't exist and what the real ones are. Costs an extra call only then;
        /// if the retry is unusable, <paramref name="first"/> (already safe) stands.
        /// </summary>
        public async Task<PlanAttempt> CorrectAsync(string question, PlanAttempt first)
        {
            var feedback = await FeedbackAsync(first.Rejected);
            logger.LogInformation("NlSummarize: retrying plan generation after rejected values → {fb}", feedback);

            var retry = await GenerateAsync(NlPrompt.Correction(question, feedback));
            if (retry.Plan is null) return first;
            return retry with
            {
                Notes = NlPlanValidator.MergeRetryNotes(
                    first.Rejected, first.Notes, retry.Plan.Filters, retry.Rejected, retry.Notes, retry.Unmatched),
            };
        }

        /// <summary>
        /// The real values each String column's filters are checked against. A column whose values were
        /// all read is checked against them. Any other (too many values to read, beyond the columns read,
        /// or unreadable up front) is checked by looking up just the plan's values — when all its filters
        /// match exact values; a Contains or range filter can't be proven wrong by a lookup, so such a
        /// column stays unchecked.
        /// </summary>
        private async Task<Dictionary<string, List<string>>> CheckableValuesAsync(IReadOnlyList<FilterDimensionDto> filters)
        {
            var checkable = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in filters.Where(f => schema.IsString(f.FieldName))
                         .GroupBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase))
            {
                if (values.Complete(group.Key) is { } all)
                {
                    checkable[group.Key] = all;
                    continue;
                }
                if (group.Any(f => f.Op is "Contains" or "GreaterThan" or "LessThan" or "Between"))
                    continue;

                var matches = await source.LookupAsync(group.Key, group.SelectMany(f => f.Values).Distinct().ToList(), ct);
                if (matches is not null)
                    checkable[group.Key] = matches;
            }
            return checkable;
        }

        /// <summary>One line per rejected filter: the values that don't exist, and what to use instead.</summary>
        private async Task<string> FeedbackAsync(IReadOnlyList<(string FieldName, List<string> Values)> rejected)
        {
            var lines = new List<string>();
            foreach (var (field, rejectedValues) in rejected)
            {
                var bad = string.Join(", ", rejectedValues.Select(v => $"\"{v}\""));
                if (values.Listed(field) is { } allowed)
                {
                    lines.Add($"- {field}: {bad} — allowed values: {string.Join(" | ", allowed)}");
                    continue;
                }

                // Too many values to list — offer real values that resemble what the LLM wrote.
                var candidates = await FindCandidatesAsync(field, rejectedValues);
                lines.Add(candidates.Count > 0
                    ? $"- {field}: {bad} — closest real values: {string.Join(" | ", candidates)}"
                    : $"- {field}: {bad} — no similar value exists in this column");
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Real values resembling the rejected ones, for a column too large to list: search by each whole
        /// value, then by its longest word, and rank what comes back.
        /// </summary>
        private async Task<List<string>> FindCandidatesAsync(string field, List<string> rejectedValues)
        {
            var found = new List<string>();
            foreach (var value in rejectedValues.Take(MaxValuesSearched))
            {
                var hits = await source.SearchAsync(field, value.Trim(), MaxRetryCandidates, ct);
                if (hits.Count == 0)
                {
                    var word = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).MaxBy(w => w.Length);
                    if (word is { Length: >= 3 } && word != value.Trim())
                        hits = await source.SearchAsync(field, word, MaxRetryCandidates, ct);
                }
                found.AddRange(NlPlanValidator.SuggestCandidates(hits, value));
            }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRetryCandidates).ToList();
        }
    }
}
