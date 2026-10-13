using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>
    /// The reported failure: 「台灣各產品類別的營收」 returned 0 rows filtered on
    /// <c>Country In [Taiwan]</c>. Phase 1 picked ProductCategory (a grouping column) instead of
    /// Country, so Phase 2 never saw Country's real values and invented "Taiwan". Meanwhile a
    /// "filter was ignored" note fired for the unused ProductCategory on every such query, even
    /// when nothing had been dropped.
    ///
    /// These tests pin the deterministic post-check: a value that isn't in the data never reaches
    /// the executed plan, and a note appears only when a value was actually removed.
    /// </summary>
    public class NlPlanValidatorTests
    {
        private static readonly Dictionary<string, List<string>> Real = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Country"]         = ["Australia", "Brazil", "Canada", "Japan", "UK", "USA"],
            ["CustomerSegment"] = ["Enterprise", "SMB"],
        };

        private static FilterDimensionDto Filter(string field, string? op, params string[] values) =>
            new() { FieldName = field, Op = op, Values = [.. values] };

        [Fact]
        public void InventedValue_FilterIsDroppedWithChineseNote()
        {
            var (filters, notes, rejected) = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "Taiwan")], Real);

            Assert.Empty(filters);
            var (field, values) = Assert.Single(rejected);
            Assert.Equal("Country", field);
            Assert.Equal(["Taiwan"], values);
            var note = Assert.Single(notes);
            Assert.Equal("資料中找不到「Taiwan」（Country 欄位），已忽略此篩選條件。", note);
        }

        [Fact]
        public void RealValue_IsKeptWithoutNote()
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "USA")], Real);

            Assert.Equal(["USA"], Assert.Single(filters).Values);
            Assert.Empty(notes);
        }

        [Fact]
        public void CaseMismatch_IsRewrittenToCanonicalSpelling()
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues([Filter("Country", null, "usa", "JAPAN")], Real);

            Assert.Equal(["USA", "Japan"], Assert.Single(filters).Values);
            Assert.Empty(notes);
        }

        [Fact]
        public void PartiallyInvented_KeepsRealValuesAndNotesTheRest()
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues(
                [Filter("Country", "In", "USA", "Taiwan", "Korea")], Real);

            Assert.Equal(["USA"], Assert.Single(filters).Values);
            Assert.Equal("資料中找不到「Taiwan」、「Korea」（Country 欄位），已從篩選條件中移除。", Assert.Single(notes));
        }

        [Fact]
        public void ColumnWithoutKnownValues_IsLeftUnvalidated()
        {
            // Not in the dictionary: a non-String column, or a value list that couldn't be fetched completely.
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues([Filter("Region", "In", "Atlantis")], Real);

            Assert.Equal(["Atlantis"], Assert.Single(filters).Values);
            Assert.Empty(notes);
        }

        [Theory]
        [InlineData("GreaterThan")]
        [InlineData("LessThan")]
        [InlineData("Between")]
        public void RangeOps_AreNotValueChecked(string op)
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues([Filter("Country", op, "M")], Real);

            Assert.Single(filters);
            Assert.Empty(notes);
        }

        [Fact]
        public void Contains_KeepsSubstringOfARealValue()
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues(
                [Filter("Country", "Contains", "aus", "zzz")], Real);

            Assert.Equal(["aus"], Assert.Single(filters).Values);
            Assert.Single(notes);
        }

        [Fact]
        public void OnlyTheInventedFilterIsDropped_OthersSurvive()
        {
            var (filters, notes, _) = NlPlanValidator.ValidateFilterValues(
                [Filter("Country", "In", "Taiwan"), Filter("CustomerSegment", "Equals", "enterprise")], Real);

            var kept = Assert.Single(filters);
            Assert.Equal("CustomerSegment", kept.FieldName);
            Assert.Equal(["Enterprise"], kept.Values);
            Assert.Single(notes);
        }

        [Fact]
        public void Interpretation_IsPlainChinese()
        {
            var plan = new SummarizeRequestDto
            {
                ViewBy   = [new GroupFieldDto { Type = "DateTrunc", FieldName = "OrderDate", Alias = "Month", Granularity = "Month" }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum, Alias = "營收" }],
                Filters  = [Filter("Country", "In", "USA")],
            };

            Assert.Equal("依月份分組，加總營收｜篩選：Country＝USA", NlPlanValidator.BuildInterpretation(plan));
        }
    }
}
