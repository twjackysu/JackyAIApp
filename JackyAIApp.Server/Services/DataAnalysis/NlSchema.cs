namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// The columns a question is planned against, sorted by what the planner does with each: every
    /// column is named in the prompt, a String column's filters are checked against its real values,
    /// and a date column's latest date goes in the prompt.
    /// </summary>
    internal sealed class NlSchema
    {
        /// <summary>At most this many date columns have their latest date looked up (one query each).</summary>
        internal const int MaxDateColumns = 3;

        private readonly HashSet<string> _names;
        private readonly HashSet<string> _strings;

        private NlSchema(string fieldList, HashSet<string> names, List<string> stringColumns, List<string> dateColumns)
        {
            FieldList = fieldList;
            _names = names;
            StringColumns = stringColumns;
            _strings = stringColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            DateColumns = dateColumns;
        }

        /// <summary>Every column with its type, as the prompt lists them: <c>Region(String), Amount(Decimal)</c>.</summary>
        public string FieldList { get; }

        /// <summary>The String columns, in the source's order.</summary>
        public IReadOnlyList<string> StringColumns { get; }

        /// <summary>The first <see cref="MaxDateColumns"/> date columns.</summary>
        public IReadOnlyList<string> DateColumns { get; }

        public bool Has(string column) => _names.Contains(column);

        public bool IsString(string column) => _strings.Contains(column);

        /// <summary>The text with every control character (a line break included) as a space.</summary>
        internal static string OneLine(string text) => new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

        public static NlSchema From(IReadOnlyList<(string Name, string CanonicalType)> fields) => new(
            // A name is the data's words: a line break in one must not start a section of the prompt.
            string.Join(", ", fields.Select(f => $"{OneLine(f.Name)}({f.CanonicalType})")),
            fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            // Only String columns are candidates for value-based filtering; numbers/dates use ranges.
            fields.Where(f => string.Equals(f.CanonicalType, "String", StringComparison.OrdinalIgnoreCase)).Select(f => f.Name).ToList(),
            // Their latest date lets the LLM resolve relative dates ("latest", "this month").
            fields.Where(f => f.CanonicalType is "DateTime" or "DateTimeOffset" or "Date").Select(f => f.Name).Take(MaxDateColumns).ToList());
    }
}
