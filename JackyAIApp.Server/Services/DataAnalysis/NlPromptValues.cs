namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// What is known of the String columns' real values: read up front (at most
    /// <see cref="PerColumnLimit"/> a column), and listed in the prompt while they fit
    /// <see cref="PromptTotal"/> across all columns. Read straight from the data, so nothing about a
    /// particular dataset is hard-coded in the prompt: whatever codes a column really holds ("LATAM",
    /// "Online"…) are what the LLM maps the user's wording onto. A column with more values
    /// (CustomerName…) is left out of the prompt and checked by lookup instead.
    /// </summary>
    internal sealed class NlPromptValues
    {
        /// <summary>A String column with at most this many distinct values has them all listed in the prompt.</summary>
        internal const int PerColumnLimit = 300;

        /// <summary>Total distinct values listed in the prompt across all columns (keeps wide tables bounded).</summary>
        internal const int PromptTotal = 2000;

        /// <summary>
        /// A column is listed only when every value is at most this long and on one line: a description or notes column
        /// is no filter's vocabulary, would cost the author the whole prompt again on every question, and a value holding
        /// a line break could start a section or rule of its own (the values are the data's words, not ours). Such a column
        /// is checked by lookup instead.
        /// </summary>
        internal const int MaxListedValueLength = 100;

        /// <summary>Total characters of values listed in the prompt across all columns.</summary>
        internal const int PromptCharTotal = 40_000;

        private readonly IReadOnlyDictionary<string, ColumnValues> _read;
        private readonly HashSet<string> _listed;

        private NlPromptValues(IReadOnlyDictionary<string, ColumnValues> read, HashSet<string> listed, List<string> lines)
        {
            _read = read;
            _listed = listed;
            Lines = lines;
        }

        /// <summary>One line per listed column: <c>- Region: APAC | EMEA</c>.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>
        /// Lists each column in order while its values fit what is left of the total; one that does not
        /// fit is skipped, and a later, smaller one may still be listed.
        /// </summary>
        /// <param name="stringColumns">The String columns, in the source's order.</param>
        /// <param name="read">The values read per column; null when they could not be read in time.</param>
        public static NlPromptValues Select(IReadOnlyList<string> stringColumns, IReadOnlyDictionary<string, ColumnValues>? read)
        {
            read ??= new Dictionary<string, ColumnValues>(StringComparer.OrdinalIgnoreCase);
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();
            var budget = PromptTotal;
            var charBudget = PromptCharTotal;
            foreach (var column in stringColumns)
            {
                if (!read.TryGetValue(column, out var cv) || cv.IsTruncated || cv.Values.Count == 0 || cv.Values.Count > budget
                    || column.Any(char.IsControl)
                    || cv.Values.Any(v => v.Length > MaxListedValueLength || v.Any(char.IsControl)))
                    continue;
                var chars = cv.Values.Sum(v => v.Length + 3);
                if (chars > charBudget) continue;

                lines.Add($"- {column}: {string.Join(" | ", cv.Values)}");
                listed.Add(column);
                budget -= cv.Values.Count;
                charBudget -= chars;
            }
            return new NlPromptValues(read, listed, lines);
        }

        /// <summary>The values the prompt listed for <paramref name="column"/>, or null when it listed none.</summary>
        public List<string>? Listed(string column) => _listed.Contains(column) ? _read[column].Values : null;

        /// <summary>All of a column's values when they were all read (listed or not), else null.</summary>
        public List<string>? Complete(string column) =>
            _read.TryGetValue(column, out var cv) && !cv.IsTruncated && cv.Values.Count > 0 ? cv.Values : null;
    }
}
