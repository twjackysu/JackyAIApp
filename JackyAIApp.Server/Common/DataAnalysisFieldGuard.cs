using System.Text.RegularExpressions;
using DataAnalysisLibrary.Core.Engine;
using DataAnalysisLibrary.Core.Models.Requests;
using JackyAIApp.Server.DTO.DataAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Schema whitelist for engine-backed DataAnalysis queries. The query endpoints stay anonymous and
    /// the default source is the app's own database, so every field name a caller sends must be a real
    /// column of the resolved table, and every alias must be plain text, before anything reaches the
    /// engine. The library escapes identifiers itself; this is the second layer, so a name it would
    /// mis-quote can never be put in front of it in the first place.
    /// Session queries don't come through here: the session store is in-memory LINQ and builds no SQL.
    /// </summary>
    public static partial class DataAnalysisFieldGuard
    {
        /// <summary>How long the default source's column list is reused. Custom sources are re-read each time.</summary>
        private static readonly TimeSpan SchemaCacheTtl = TimeSpan.FromMinutes(10);

        /// <summary>SQL Server's identifier limit (sysname); also bounds what goes into the error message.</summary>
        private const int MaxAliasLength = 128;

        /// <summary>
        /// Letters and digits of any script (the NL planner writes aliases such as "銷售額"), spaces, and
        /// a few ASCII marks. Every character that can close an identifier or a string, or end a
        /// statement, in any connector — ] " ' ` \ ; — is ASCII and not in this set.
        /// </summary>
        [GeneratedRegex(@"^[\p{L}\p{M}\p{N} _\-().%/]+$")]
        private static partial Regex SafeAliasPattern();

        /// <summary>
        /// The resolved table's column names, compared case-insensitively, or null when the schema
        /// couldn't be read (callers then refuse the query). Pass <paramref name="cache"/> only for the
        /// default source — its table is fixed, while a custom source's table can change under its owner.
        /// </summary>
        public static async Task<IReadOnlySet<string>?> LoadFieldNamesAsync(
            DataAnalysisEngine engine, string sourceKey, string tableName, IMemoryCache? cache, CancellationToken ct)
        {
            var key = $"dataanalysis:schema:{sourceKey}:{tableName}";
            if (cache is not null && cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
                return cached;

            var result = await engine.FieldListAsync(new FieldListRequest { SourceKey = sourceKey, TableName = tableName }, ct);
            if (!result.IsSuccess) return null;

            var names = result.Value!.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (cache is not null && names.Count > 0)
                cache.Set(key, (IReadOnlySet<string>)names, SchemaCacheTtl);
            return names;
        }

        /// <summary>
        /// The reason to reject a summarize plan (ViewBy, Measures and Filters field names; ViewBy and
        /// Measures aliases), or null when it may run. Also used for saved dashboard widgets and NL plans.
        /// </summary>
        public static string? Check(IReadOnlySet<string> fields, SummarizeRequestDto dto)
        {
            var viewBy   = dto.ViewBy ?? [];
            var measures = dto.Measures ?? [];
            return FirstError(fields,
                viewBy.Select(v => v.FieldName)
                    .Concat(measures.Select(m => m.FieldName))
                    .Concat((dto.Filters ?? []).Select(f => f.FieldName)),
                viewBy.Select(v => v.Alias).Concat(measures.Select(m => m.Alias)));
        }

        /// <summary>The reason to reject a details/export request (SelectFields, OrderBy and Filters field names), or null.</summary>
        public static string? Check(IReadOnlySet<string> fields, DetailsRequestDto dto) =>
            FirstError(fields,
                (dto.SelectFields ?? [])
                    .Concat((dto.OrderBy ?? []).Select(o => o.FieldName))
                    .Concat((dto.Filters ?? []).Select(f => f.FieldName)),
                []);

        /// <summary>The reason to reject a single field name (distinct values, max date), or null.</summary>
        public static string? CheckField(IReadOnlySet<string> fields, string? field) =>
            FirstError(fields, [field], []);

        /// <summary>
        /// Whether an alias may be sent as an output column name. No alias (null or blank) is safe —
        /// the mapping drops it and the column takes the field's name.
        /// </summary>
        public static bool IsSafeAlias(string? alias) =>
            string.IsNullOrWhiteSpace(alias) ||
            (alias.Length <= MaxAliasLength && !alias.Contains("--") && SafeAliasPattern().IsMatch(alias));

        /// <summary>
        /// An LLM-written plan with any unsafe alias dropped, so the column falls back to its field name
        /// instead of the whole (already charged) question failing over a label.
        /// </summary>
        public static SummarizeRequestDto WithoutUnsafeAliases(SummarizeRequestDto plan) => plan with
        {
            ViewBy   = plan.ViewBy.Select(v => IsSafeAlias(v.Alias) ? v : v with { Alias = null }).ToList(),
            Measures = plan.Measures.Select(m => IsSafeAlias(m.Alias) ? m : m with { Alias = null }).ToList(),
        };

        private static string? FirstError(IReadOnlySet<string> fields, IEnumerable<string?> names, IEnumerable<string?> aliases)
        {
            foreach (var name in names)
                if (name is null || !fields.Contains(name))
                    return $"Unknown field '{Shorten(name)}'.";

            foreach (var alias in aliases)
                if (!IsSafeAlias(alias))
                    return $"Alias '{Shorten(alias)}' may only contain letters, digits, spaces and _ - ( ) . % /";

            return null;
        }

        private static string Shorten(string? s) =>
            s is null ? "" : s.Length <= 64 ? s : s[..64] + "…";
    }
}
