namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>The wording the NL planner sends the LLM, in one place.</summary>
    internal static class NlPrompt
    {
        /// <summary>The system prompt: the summarize rules, the columns, today (UTC), and the two blocks below.</summary>
        public static string System(string rules, string fieldList, DateOnly today, string valuesBlock, string datesBlock) =>
            $"{rules}\n\nAVAILABLE COLUMNS:\n{fieldList}\nTODAY: {today:yyyy-MM-dd}{valuesBlock}{datesBlock}";

        /// <summary>The real values the plan must filter with; empty when no column is listed.</summary>
        public static string ValuesBlock(IReadOnlyList<string> lines) =>
            lines.Count > 0
                ? "\n\nACTUAL VALUES for filterable columns — when filtering on these columns you MUST use exactly these values (match the user's intent to the closest value — synonyms, translations and abbreviations included; if none matches, omit that filter AND put the user's wording in \"unmatched\"):\n"
                  + string.Join("\n", lines)
                : "";

        /// <summary>Each date column's latest date with its relative ranges (<see cref="NlDateRanges.Describe"/>); empty when none is known.</summary>
        public static string DatesBlock(IReadOnlyList<string> lines) =>
            lines.Count > 0
                ? "\n\nDATA DATE RANGE — the most recent date present in each date column, with the relative ranges "
                  + "already computed from it (NOT from today's date). For \"latest\", \"this month\", \"last quarter\" "
                  + "and the like, copy the matching range verbatim:\n"
                  + string.Join("\n", lines)
                : "";

        /// <summary>The question again, with which of the plan's values do not exist and what to use instead.</summary>
        public static string Correction(string question, string feedback) =>
            $"{question}\n\n[CORRECTION] Your previous plan filtered on values that do not exist in the data:\n{feedback}\n"
            + "Replace each with the allowed value that matches the user's intent (synonym, translation or abbreviation); "
            + "if none matches, omit that filter and put the user's wording for it in \"unmatched\". "
            + "Return the complete corrected JSON plan.";
    }
}
