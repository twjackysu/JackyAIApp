using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Deterministic post-processing for the NL summarize plan produced by the LLM: filter-value
    /// validation against the real data, and the user-facing (Traditional Chinese) interpretation line.
    /// </summary>
    public static class NlPlanValidator
    {
        /// <summary>
        /// Checks every value-matching filter (In / NotIn / Equals / Contains) on a String column
        /// against that column's real distinct values. Matching is case-insensitive and a match is
        /// rewritten to the data's canonical spelling; values that match nothing are removed, and a
        /// filter left with no values is dropped entirely. One note is produced per filter that lost
        /// values — and only then — so a plan that needed no correction yields no notes.
        /// This is the last line of defence: whether or not Phase 1 offered the column's real values
        /// to the LLM, the executed plan can never filter on a value that doesn't exist.
        /// </summary>
        /// <param name="filters">The plan's filters as produced by the LLM.</param>
        /// <param name="realValues">
        /// Distinct values per String column. A column that is absent (non-String, range filter,
        /// or a list that couldn't be fetched completely) is left unvalidated.
        /// </param>
        public static (List<FilterDimensionDto> Filters, List<string> Notes) ValidateFilterValues(
            IReadOnlyList<FilterDimensionDto> filters,
            IReadOnlyDictionary<string, List<string>> realValues)
        {
            var kept  = new List<FilterDimensionDto>();
            var notes = new List<string>();

            foreach (var filter in filters)
            {
                if (!IsValueMatchOp(filter.Op) || !realValues.TryGetValue(filter.FieldName, out var real))
                {
                    kept.Add(filter);
                    continue;
                }

                var good = new List<string>();
                var bad  = new List<string>();
                foreach (var value in filter.Values)
                {
                    var match = filter.Op == "Contains"
                        ? (real.Any(r => r.Contains(value, StringComparison.OrdinalIgnoreCase)) ? value : null)
                        : real.FirstOrDefault(r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase));

                    if (match is null) bad.Add(value);
                    else if (!good.Contains(match, StringComparer.OrdinalIgnoreCase)) good.Add(match);
                }

                if (bad.Count > 0)
                {
                    var shown = string.Join("、", bad.Select(v => $"「{v}」"));
                    notes.Add(good.Count > 0
                        ? $"資料中找不到{shown}（{filter.FieldName} 欄位），已從篩選條件中移除。"
                        : $"資料中找不到{shown}（{filter.FieldName} 欄位），已忽略此篩選條件。");
                }

                if (good.Count > 0)
                    kept.Add(filter with { Values = good });
            }

            return (kept, notes);
        }

        /// <summary>Ops whose values must equal (or be contained in) a real column value; ranges are excluded.</summary>
        /// <remarks>MapFilter treats null / unrecognised ops as "In", so those are validated too.</remarks>
        private static bool IsValueMatchOp(string? op) =>
            op is not ("GreaterThan" or "LessThan" or "Between");

        /// <summary>
        /// Plain-language description of the executed plan, e.g.
        /// 「依月份分組，加總營收｜篩選：Country＝USA」.
        /// </summary>
        public static string BuildInterpretation(SummarizeRequestDto plan)
        {
            var viewBy   = string.Join("、", plan.ViewBy.Select(DescribeGroup));
            var measures = string.Join("、", plan.Measures.Select(m =>
                $"{DescribeAggregation(m.Aggregation.ToString())}{m.Alias ?? m.FieldName}"));
            var filters  = plan.Filters is { Count: > 0 }
                ? "｜篩選：" + string.Join("；", plan.Filters.Select(DescribeFilter))
                : "";
            return $"依{viewBy}分組，{measures}{filters}";
        }

        /// <summary>A date bucket reads as its granularity (「月份」); its alias is usually just "Month".</summary>
        private static string DescribeGroup(GroupFieldDto g) =>
            g.Type == "DateTrunc" ? DescribeGranularity(g.Granularity) : g.Alias ?? g.FieldName;

        private static string DescribeGranularity(string? granularity) => granularity switch
        {
            "Year"    => "年度",
            "Quarter" => "季度",
            "Month"   => "月份",
            "Week"    => "週",
            "Hour"    => "小時",
            _         => "日期",
        };

        private static string DescribeAggregation(string aggregation) => aggregation switch
        {
            "Sum"           => "加總",
            "Count"         => "計數",
            "DistinctCount" => "不重複計數",
            "Average"       => "平均",
            "Min"           => "最小值",
            "Max"           => "最大值",
            _               => aggregation + " ",
        };

        private static string DescribeFilter(FilterDimensionDto f)
        {
            var values = string.Join("、", f.Values.Take(3)) + (f.Values.Count > 3 ? "…" : "");
            return f.Op switch
            {
                "NotIn"       => $"{f.FieldName} 不是 {values}",
                "Contains"    => $"{f.FieldName} 包含 {values}",
                "GreaterThan" => $"{f.FieldName} > {values}",
                "LessThan"    => $"{f.FieldName} < {values}",
                "Between"     => f.Values.Count >= 2
                    ? $"{f.FieldName} 介於 {f.Values[0]} ~ {f.Values[1]}"
                    : $"{f.FieldName} 介於 {values}",
                _             => $"{f.FieldName}＝{values}",
            };
        }
    }
}
