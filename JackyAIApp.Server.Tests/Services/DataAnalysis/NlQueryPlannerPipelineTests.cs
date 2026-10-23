using System.Text.RegularExpressions;
using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>
    /// The whole planning pipeline of <see cref="NlQueryPlanner.BuildPlanAsync"/>: what the LLM is told
    /// (the columns, their real values within a budget, the data's latest dates), how its plan is
    /// checked against the data, and the one correction it gets when a filter value does not exist.
    /// </summary>
    public class NlQueryPlannerPipelineTests
    {
        private const string Rules = "SUMMARIZE RULES";
        private const string Question = "total amount by region in Asia";

        private readonly Mock<IOpenAIPromptService> _openAI = new();
        private readonly Mock<INlValueSource> _values = new();
        private readonly Queue<NlPlanDto?> _plans = new();
        private readonly List<(string System, string User)> _asked = [];
        private Dictionary<string, ColumnValues>? _distinct = new(StringComparer.OrdinalIgnoreCase);

        private static readonly List<(string Name, string CanonicalType)> Fields =
            [("Region", "String"), ("Channel", "String"), ("Customer", "String"), ("Amount", "Decimal"), ("OrderDate", "DateTime")];

        public NlQueryPlannerPipelineTests()
        {
            _openAI.Setup(x => x.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .Callback((string system, string user, string? _, IEnumerable<(string, string)>? _, string? _) => _asked.Add((system, user)))
                .ReturnsAsync(() =>
                {
                    var plan = _plans.Count > 0 ? _plans.Dequeue() : null;
                    return (plan, plan is null ? "no plan" : null);
                });
            _values.Setup(v => v.DistinctAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _distinct);
            _values.Setup(v => v.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        }

        private Task<NlPlanResult> PlanAsync(
            List<(string Name, string CanonicalType)>? fields = null, Func<string, Task<string?>>? maxDate = null) =>
            new NlQueryPlanner(NullLogger<NlQueryPlanner>.Instance, _openAI.Object).BuildPlanAsync(
                fields ?? Fields, Question, Rules, _values.Object, maxDate ?? (_ => Task.FromResult<string?>(null)), CancellationToken.None);

        private static NlPlanDto Plan(List<FilterDimensionDto>? filters = null, List<string>? unmatched = null) => new()
        {
            ViewBy = [new GroupFieldDto { FieldName = "Region" }],
            Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum }],
            Filters = filters,
            Unmatched = unmatched,
        };

        private static FilterDimensionDto Filter(string field, string op, params string[] values) =>
            new() { FieldName = field, Op = op, Values = [.. values] };

        private static ColumnValues Listed(params string[] values) => new([.. values], false);

        private static ColumnValues TooManyToList() => new(["a", "b"], true);

        private static List<string> Many(string prefix, int count) => [.. Enumerable.Range(1, count).Select(i => $"{prefix}{i}")];

        private static string WithoutToday(string system) =>
            Regex.Replace(system, @"TODAY: \d{4}-\d{2}-\d{2}", "TODAY: <today>");

        private static string Correction(string feedback) =>
            $"{Question}\n\n[CORRECTION] Your previous plan filtered on values that do not exist in the data:\n{feedback}\n"
            + "Replace each with the allowed value that matches the user's intent (synonym, translation or abbreviation); "
            + "if none matches, omit that filter and put the user's wording for it in \"unmatched\". "
            + "Return the complete corrected JSON plan.";

        // ── what the LLM is told ────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ThePrompt_IsTheRules_TheColumns_Today_AndTheRealValuesOfEachFullyKnownColumn()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Region"] = Listed("APAC", "EMEA"),
                ["Channel"] = TooManyToList(),
                ["Customer"] = Listed(),
            };
            _plans.Enqueue(Plan());

            await PlanAsync();

            var (system, user) = Assert.Single(_asked);
            Assert.Equal(
                "SUMMARIZE RULES\n\nAVAILABLE COLUMNS:\nRegion(String), Channel(String), Customer(String), Amount(Decimal), OrderDate(DateTime)\n"
                + "TODAY: <today>"
                + "\n\nACTUAL VALUES for filterable columns — when filtering on these columns you MUST use exactly these values "
                + "(match the user's intent to the closest value — synonyms, translations and abbreviations included; if none matches, "
                + "omit that filter AND put the user's wording in \"unmatched\"):\n"
                + "- Region: APAC | EMEA",
                WithoutToday(system));
            Assert.Matches(@"TODAY: \d{4}-\d{2}-\d{2}", system);
            Assert.Equal(Question, user);
        }

        [Fact]
        public async Task ColumnsAreListed_WhileTheirValuesFitTheTotalOf2000_AndALaterSmallerOneStillFits()
        {
            var fields = Enumerable.Range(1, 9).Select(i => ($"C{i}", "String")).Append(("Amount", "Decimal")).ToList();
            _distinct = new(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i <= 7; i++) _distinct[$"C{i}"] = new(Many($"c{i}v", 250), false);   // 1750 in all
            _distinct["C8"] = new(Many("c8v", 300), false);   // past the 250 left
            _distinct["C9"] = new(Many("c9v", 250), false);   // exactly what is left
            _plans.Enqueue(new NlPlanDto
            {
                ViewBy = [new GroupFieldDto { FieldName = "C1" }],
                Measures = [new MeasureFieldDto { FieldName = "Amount", Aggregation = AggregationType.Sum }],
            });

            await PlanAsync(fields);

            var system = Assert.Single(_asked).System;
            Assert.Equal(["C1", "C2", "C3", "C4", "C5", "C6", "C7", "C9"],
                Regex.Matches(system, @"^- (C\d): ", RegexOptions.Multiline).Select(m => m.Groups[1].Value));
        }

        [Fact]
        public async Task OnlyTheFirst30StringColumns_AreRead_AtMost300ValuesEach()
        {
            var fields = Enumerable.Range(1, 35).Select(i => ($"S{i}", "String")).Append(("Amount", "Decimal")).ToList();
            _plans.Enqueue(null);

            await PlanAsync(fields);

            _values.Verify(v => v.DistinctAsync(
                It.Is<IReadOnlyList<string>>(f => f.SequenceEqual(Enumerable.Range(1, 30).Select(i => $"S{i}"))), 300,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ThePrompt_GivesTheLatestDateOfUpToThreeDateColumns()
        {
            var fields = new List<(string, string)>
            {
                ("Region", "String"), ("Amount", "Decimal"), ("Ordered", "DateTime"), ("Shipped", "Date"),
                ("Paid", "DateTimeOffset"), ("Closed", "DateTime"),
            };
            var asked = new List<string>();
            _plans.Enqueue(Plan());

            await PlanAsync(fields, col =>
            {
                asked.Add(col);
                return Task.FromResult(col == "Shipped" ? null : (string?)"2026-09-15");
            });

            Assert.Equal(["Ordered", "Shipped", "Paid"], asked);
            Assert.EndsWith(
                "\n\nDATA DATE RANGE — the most recent date present in each date column, with the relative ranges already "
                + "computed from it (NOT from today's date). For \"latest\", \"this month\", \"last quarter\" and the like, "
                + "copy the matching range verbatim:\n"
                + NlDateRanges.Describe("Ordered", "2026-09-15") + "\n" + NlDateRanges.Describe("Paid", "2026-09-15"),
                Assert.Single(_asked).System);
        }

        // ── the plan ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task APlanWithoutGroupingOrMeasures_IsABadRequest()
        {
            _plans.Enqueue(Plan() with { Measures = [] });

            var result = await PlanAsync();

            Assert.Equal((null, ErrorCodes.BadRequest, "AI could not interpret your query. Please try rephrasing."),
                (result.Plan, result.Error, result.ErrorMessage));
            Assert.Empty(result.Notes);
            Assert.Empty(result.Unmatched);
        }

        [Fact]
        public async Task UnmatchedConditions_ComeBack_AndBecomeNotes()
        {
            _plans.Enqueue(Plan(unmatched: ["越南", " ", "越南"]));

            var result = await PlanAsync();

            Assert.Equal(["越南"], result.Unmatched);
            Assert.Equal(["資料中找不到符合「越南」的值，已忽略此條件，以下結果未套用此篩選。"], result.Notes);
        }

        [Fact]
        public async Task WithNoValuesReadable_TheQuestionStillRuns_AndItsFiltersAreCheckedByLookup()
        {
            _distinct = null;
            _values.Setup(v => v.LookupAsync("Region", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(["APAC"]);
            _plans.Enqueue(Plan([Filter("Region", "In", "apac")]));

            var result = await PlanAsync();

            Assert.DoesNotContain("ACTUAL VALUES", Assert.Single(_asked).System);
            Assert.Equal(["APAC"], Assert.Single(result.Plan!.Filters!).Values);
            Assert.Empty(result.Notes);
        }

        [Fact]
        public async Task ARangeOrContainsFilterOnAColumnTooLargeToList_IsLeftUnchecked()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Customer"] = TooManyToList() };
            _plans.Enqueue(Plan([Filter("Customer", "Contains", "Acm"), Filter("Customer", "In", "Nobody")]));

            var result = await PlanAsync();

            _values.Verify(v => v.LookupAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal(2, result.Plan!.Filters!.Count);
            Assert.Single(_asked);
        }

        [Fact]
        public async Task ANumericColumnsFilter_IsNotChecked()
        {
            _plans.Enqueue(Plan([Filter("Amount", "GreaterThan", "100")]));

            var result = await PlanAsync();

            Assert.Equal("100", Assert.Single(Assert.Single(result.Plan!.Filters!).Values));
            _values.Verify(v => v.LookupAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── the correction ──────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task AValueNotInAListedColumn_IsCorrectedOnce_WithThatColumnsValues()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Region"] = Listed("APAC", "EMEA") };
            _plans.Enqueue(Plan([Filter("Region", "In", "Asia")]));
            _plans.Enqueue(Plan([Filter("Region", "In", "APAC")]));

            var result = await PlanAsync();

            Assert.Equal(2, _asked.Count);
            Assert.Equal(_asked[0].System, _asked[1].System);
            Assert.Equal(Correction("- Region: \"Asia\" — allowed values: APAC | EMEA"), _asked[1].User);
            Assert.Equal(["APAC"], Assert.Single(result.Plan!.Filters!).Values);
            Assert.Empty(result.Notes);   // the retry filters on Region, so the first pass's note is not carried
        }

        [Fact]
        public async Task AValueInAColumnTooLargeToList_IsCheckedByLookup_AndCorrectedWithResemblingRealValues()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Customer"] = TooManyToList() };
            _values.Setup(v => v.LookupAsync("Customer", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, IReadOnlyList<string> asked, CancellationToken _) =>
                    [.. asked.Where(a => a.Equals("Acme Corp", StringComparison.OrdinalIgnoreCase))]);
            _values.Setup(v => v.SearchAsync("Customer", "Acme", 20, It.IsAny<CancellationToken>()))
                .ReturnsAsync(["Acme Corp", "Zenith Ltd"]);
            _plans.Enqueue(Plan([Filter("Customer", "In", "Acme")]));
            _plans.Enqueue(Plan([Filter("Customer", "In", "Acme Corp")]));

            var result = await PlanAsync();

            Assert.Equal(Correction("- Customer: \"Acme\" — closest real values: Acme Corp"), _asked[1].User);
            Assert.Equal(["Acme Corp"], Assert.Single(result.Plan!.Filters!).Values);
        }

        [Fact]
        public async Task ASearchWithNoHits_TriesTheLongestWord_ThenSaysNothingSimilarExists()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Customer"] = TooManyToList() };
            _values.Setup(v => v.LookupAsync("Customer", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _plans.Enqueue(Plan([Filter("Customer", "In", "Big Acme Holdings")]));
            _plans.Enqueue(Plan());

            await PlanAsync();

            _values.Verify(v => v.SearchAsync("Customer", "Big Acme Holdings", 20, It.IsAny<CancellationToken>()), Times.Once);
            _values.Verify(v => v.SearchAsync("Customer", "Holdings", 20, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(Correction("- Customer: \"Big Acme Holdings\" — no similar value exists in this column"), _asked[1].User);
        }

        [Fact]
        public async Task AtMostThreeRejectedValues_AreSearchedFor()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Customer"] = TooManyToList() };
            _values.Setup(v => v.LookupAsync("Customer", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            _plans.Enqueue(Plan([Filter("Customer", "In", "Ann", "Bob", "Cid", "Dee")]));
            _plans.Enqueue(Plan());

            await PlanAsync();

            _values.Verify(v => v.SearchAsync("Customer", It.IsAny<string>(), 20, It.IsAny<CancellationToken>()), Times.Exactly(3));
            _values.Verify(v => v.SearchAsync("Customer", "Dee", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ARetryThatNamesAnUnknownField_LeavesTheFirstPlan_WithItsNotes()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Region"] = Listed("APAC", "EMEA") };
            _plans.Enqueue(Plan([Filter("Region", "In", "Asia", "APAC")]));
            _plans.Enqueue(Plan() with { ViewBy = [new GroupFieldDto { FieldName = "Continent" }] });

            var result = await PlanAsync();

            Assert.Equal(2, _asked.Count);
            Assert.Equal(["APAC"], Assert.Single(result.Plan!.Filters!).Values);
            Assert.Equal(["資料中找不到「Asia」（Region 欄位），已從篩選條件中移除。"], result.Notes);
        }

        [Fact]
        public async Task ARetryWithNoPlan_LeavesTheFirstPlan()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase) { ["Region"] = Listed("APAC", "EMEA") };
            _plans.Enqueue(Plan([Filter("Region", "In", "Asia")]));
            _plans.Enqueue(null);

            var result = await PlanAsync();

            Assert.Empty(result.Plan!.Filters!);
            Assert.Equal(["資料中找不到「Asia」（Region 欄位），已忽略此篩選條件。"], result.Notes);
        }

        [Fact]
        public async Task ARetryThatStillMisses_ReportsItsOwnNotes_AndKeepsAFirstNoteOnlyForAColumnItDropped()
        {
            _distinct = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Region"] = Listed("APAC", "EMEA"),
                ["Channel"] = Listed("Online", "Retail"),
            };
            _plans.Enqueue(Plan([Filter("Region", "In", "Asia"), Filter("Channel", "In", "Web")]));
            _plans.Enqueue(Plan([Filter("Region", "In", "Oceania")]));

            var result = await PlanAsync();

            Assert.Equal(Correction(
                "- Region: \"Asia\" — allowed values: APAC | EMEA\n- Channel: \"Web\" — allowed values: Online | Retail"), _asked[1].User);
            Assert.Equal(
                ["資料中找不到「Web」（Channel 欄位），已忽略此篩選條件。", "資料中找不到「Oceania」（Region 欄位），已忽略此篩選條件。"],
                result.Notes);
        }
    }
}
