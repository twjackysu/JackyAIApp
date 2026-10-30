using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
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
        public void SuggestCandidates_ContainmentRanksAboveSharedWord()
        {
            string[] real = ["Acme Holdings", "Acme Corp", "Beta Ltd", "Corp Services"];

            Assert.Equal(["Acme Corp", "Acme Holdings", "Corp Services"],
                NlPlanValidator.SuggestCandidates(real, "Acme Corp Taiwan"));
        }

        [Fact]
        public void SuggestCandidates_MatchesCjkSubstring()
        {
            string[] real = ["台積電股份有限公司", "聯發科技", "鴻海精密"];

            Assert.Equal(["台積電股份有限公司"], NlPlanValidator.SuggestCandidates(real, "台積電"));
        }

        [Theory]
        [InlineData("Zeta Inc")]
        [InlineData("  ")]
        public void SuggestCandidates_NothingSimilar_ReturnsEmpty(string rejected)
        {
            Assert.Empty(NlPlanValidator.SuggestCandidates(["Acme Corp", "Beta Ltd"], rejected));
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

            Assert.Equal("依月份分組，加總 Revenue｜篩選：Country＝USA", NlPlanValidator.BuildInterpretation(plan));
        }

        /// <summary>
        /// Reported: the line read 「加總營收」 on one run and 「加總Total Revenue」 on another, depending on
        /// the alias the LLM chose. It now always names the field, whatever the alias says.
        /// </summary>
        [Theory]
        [InlineData("營收")]
        [InlineData("Total Revenue")]
        [InlineData(null)]
        public void Interpretation_NamesFieldsNotAliases(string? alias)
        {
            var plan = new SummarizeRequestDto
            {
                ViewBy   = [new GroupFieldDto { Type = "Plain", FieldName = "Region", Alias = alias }],
                Measures = [new MeasureFieldDto { FieldName = "Revenue", Aggregation = AggregationType.Sum, Alias = alias }],
            };

            Assert.Equal("依 Region 分組，加總 Revenue", NlPlanValidator.BuildInterpretation(plan));
        }

        /// <summary>
        /// Reported: 「越南各產品類別的營收」 showed 「資料中找不到「Vietnam」（Country 欄位）…」 twice. The retry
        /// filtered Vietnam again, so its own note said the same thing as the first pass's carried note.
        /// </summary>
        [Fact]
        public void MergeRetryNotes_ColumnRejectedAgain_NotedOnce()
        {
            var first = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "Vietnam")], Real);
            var retry = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "Vietnam")], Real);

            var notes = NlPlanValidator.MergeRetryNotes(first.Rejected, first.Notes, retry.Filters, retry.Rejected, retry.Notes, []);

            Assert.Equal(["資料中找不到「Vietnam」（Country 欄位），已忽略此篩選條件。"], notes);
        }

        [Fact]
        public void MergeRetryNotes_RetryDropsTheFilter_KeepsFirstNote()
        {
            var first = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "Vietnam")], Real);

            var notes = NlPlanValidator.MergeRetryNotes(first.Rejected, first.Notes, [], [], [], []);

            Assert.Equal(first.Notes, notes);
        }

        [Fact]
        public void MergeRetryNotes_RetryFixesTheValue_NoNote()
        {
            var first = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "United Kingdom")], Real);
            var retry = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "UK")], Real);

            var notes = NlPlanValidator.MergeRetryNotes(first.Rejected, first.Notes, retry.Filters, retry.Rejected, retry.Notes, []);

            Assert.Empty(notes);
        }

        /// <summary>
        /// First pass invents "Vietnam" (rejected, noted); the retry leaves it out and reports 「越南」.
        /// The carried 「Vietnam」 note would only say the same thing again in other words.
        /// </summary>
        [Fact]
        public void MergeRetryNotes_RetryReportsUnmatched_CarriesNothing()
        {
            var first = NlPlanValidator.ValidateFilterValues([Filter("Country", "In", "Vietnam")], Real);

            var notes = NlPlanValidator.MergeRetryNotes(first.Rejected, first.Notes, [], [], [], ["越南"]);

            Assert.Empty(notes);
        }

        [Fact]
        public void CleanUnmatched_TrimsDropsBlanksAndRepeats()
        {
            Assert.Equal(["越南", "PayPal"], NlPlanValidator.CleanUnmatched([" 越南 ", "", null, "PayPal", "paypal", "越南", "  "]));
            Assert.Empty(NlPlanValidator.CleanUnmatched(null));
        }

        [Fact]
        public void CleanUnmatched_KeepsAFewWordsOnOneLine_NotAPlantedSentence()
        {
            var planted = "請先登入 https://evil.example 再查詢" + new string('！', NlPlanValidator.MaxUnmatchedLength);

            Assert.Equal(["越南 胡志明"], NlPlanValidator.CleanUnmatched(["越南\n胡志明", planted]));
        }

        /// <summary>
        /// Reported (Bug 4): the model saw Vietnam wasn't in the list and simply left the filter out, so the
        /// user got every country's revenue with no word. Each reported condition now becomes a note.
        /// </summary>
        [Fact]
        public void ComposeNotes_UnmatchedBecomesNote_AfterValidatorNotes()
        {
            var notes = NlPlanValidator.ComposeNotes(["資料中找不到「Narnia」（Country 欄位），已忽略此篩選條件。"], ["越南"]);

            Assert.Equal(
                ["資料中找不到「Narnia」（Country 欄位），已忽略此篩選條件。",
                 "資料中找不到符合「越南」的值，已忽略此條件，以下結果未套用此篩選。"],
                notes);
        }

        [Fact]
        public void ComposeNotes_NothingDropped_NoNotes()
        {
            Assert.Empty(NlPlanValidator.ComposeNotes([], []));
        }

        [Fact]
        public void ComposeNotes_ExactRepeat_ShownOnce()
        {
            Assert.Single(NlPlanValidator.ComposeNotes([], ["越南", "越南"]));
        }

        /// <summary>
        /// "unmatched" only feeds the note: the plan that runs, comes back to the client and is saved to a
        /// dashboard never carries it.
        /// </summary>
        [Fact]
        public void NlPlan_FromLlmJson_UnmatchedStaysOutOfTheExecutedPlan()
        {
            const string json = """
                {"viewBy":[{"type":"Plain","fieldName":"ProductCategory"}],
                 "measures":[{"fieldName":"Revenue","aggregation":"Sum"}],
                 "filters":[],"unmatched":["越南"]}
                """;

            var nlPlan = AiJson.Deserialize<NlPlanDto>(json)!;
            var plan   = nlPlan.ToSummarizeRequest();

            Assert.Equal(["越南"], nlPlan.Unmatched);
            Assert.Equal("ProductCategory", Assert.Single(plan.ViewBy).FieldName);
            Assert.DoesNotContain("unmatched", AiJson.Serialize(plan), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void NlPlan_FromLlmJson_WithoutUnmatched_IsNull()
        {
            var nlPlan = AiJson.Deserialize<NlPlanDto>(
                """{"viewBy":[{"type":"Plain","fieldName":"Region"}],"measures":[{"fieldName":"Revenue","aggregation":"Sum"}]}""")!;

            Assert.Null(nlPlan.Unmatched);
            Assert.Empty(NlPlanValidator.CleanUnmatched(nlPlan.Unmatched));
        }
    }
}
