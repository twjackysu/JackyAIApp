using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.DataAnalysis;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JackyAIApp.Server.Tests.Services.DataAnalysis
{
    /// <summary>The parts the NL planner is built from: the schema's sorting, the values listed in the prompt, and its wording.</summary>
    public class NlPlannerPartsTests
    {
        // ── NlSchema ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TheSchema_ListsEveryColumn_AndSortsOutTheStringAndDateOnes()
        {
            var schema = NlSchema.From(
            [
                ("Region", "String"), ("Amount", "Decimal"), ("Code", "string"), ("Ordered", "DateTime"),
                ("Shipped", "Date"), ("Paid", "DateTimeOffset"), ("Closed", "DateTime"),
            ]);

            Assert.Equal("Region(String), Amount(Decimal), Code(string), Ordered(DateTime), Shipped(Date), Paid(DateTimeOffset), Closed(DateTime)",
                schema.FieldList);
            Assert.Equal(["Region", "Code"], schema.StringColumns);
            Assert.Equal(["Ordered", "Shipped", "Paid"], schema.DateColumns);   // the first three
            Assert.True(schema.Has("amount") && schema.IsString("region") && schema.IsString("CODE"));
            Assert.False(schema.Has("Customer") || schema.IsString("Amount") || schema.IsString("Ordered"));
        }

        // ── NlPromptValues ──────────────────────────────────────────────────────────────────────

        private static ColumnValues Read(int count, bool truncated = false) =>
            new([.. Enumerable.Range(1, count).Select(i => $"v{i}")], truncated);

        [Fact]
        public void AColumnIsListed_OnlyWhenAllItsValuesWereRead_AndThereAreSome()
        {
            var values = NlPromptValues.Select(["Full", "Truncated", "Empty", "Unread"], new Dictionary<string, ColumnValues>
            {
                ["Full"] = Read(2), ["Truncated"] = Read(2, truncated: true), ["Empty"] = Read(0),
            });

            Assert.Equal(["- Full: v1 | v2"], values.Lines);
            Assert.Equal(["v1", "v2"], values.Listed("Full"));
            Assert.Null(values.Listed("Truncated"));
            Assert.Null(values.Listed("Unread"));
        }

        [Fact]
        public void TheTotal_IsSpentInOrder_SkippingAColumnThatNoLongerFits()
        {
            var values = NlPromptValues.Select(["A", "B", "C", "D"], new Dictionary<string, ColumnValues>
            {
                ["A"] = Read(1500), ["B"] = Read(501), ["C"] = Read(500), ["D"] = Read(1),
            });

            // B does not fit the 500 left after A, C fits them exactly, and nothing is left for D.
            Assert.Equal(["A", "C"], values.Lines.Select(l => l[2..l.IndexOf(':')]));
            Assert.Null(values.Listed("B"));
            Assert.Null(values.Listed("D"));
            // Both were read whole, so a plan's filters on them are still checked against all their values.
            Assert.Equal(501, values.Complete("B")!.Count);
            Assert.Single(values.Complete("D")!);
        }

        [Fact]
        public void NothingRead_ListsNothing_AndKnowsNothing()
        {
            var values = NlPromptValues.Select(["Region"], null);

            Assert.Empty(values.Lines);
            Assert.Null(values.Complete("Region"));
        }

        [Fact]
        public void AColumnTooLargeToRead_IsNotComplete()
        {
            var values = NlPromptValues.Select(["Customer"], new Dictionary<string, ColumnValues> { ["Customer"] = Read(300, truncated: true) });

            Assert.Null(values.Complete("Customer"));
        }

        // ── NlPrompt ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void EmptyBlocks_AddNothingToThePrompt()
        {
            Assert.Equal("", NlPrompt.ValuesBlock([]));
            Assert.Equal("", NlPrompt.DatesBlock([]));
            Assert.Equal("RULES\n\nAVAILABLE COLUMNS:\nRegion(String)\nTODAY: 2026-10-22",
                NlPrompt.System("RULES", "Region(String)", new DateOnly(2026, 10, 22), "", ""));
        }

        [Fact]
        public async Task TodayInThePrompt_IsTheUtcDate()
        {
            // 01:00 on the 23rd in Taipei is still the 22nd in UTC.
            var clock = new TestClock(new DateTimeOffset(2026, 10, 22, 17, 0, 0, TimeSpan.Zero));
            var openAI = new Mock<IOpenAIPromptService>();
            string? system = null;
            openAI.Setup(x => x.GetCompletionWithSystemPromptAsync<NlPlanDto>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IEnumerable<(string, string)>>(), It.IsAny<string>()))
                .Callback((string s, string _, string? _, IEnumerable<(string, string)>? _, string? _) => system = s)
                .ReturnsAsync((new NlPlanDto
                {
                    ViewBy = [new GroupFieldDto { FieldName = "Region" }],
                    Measures = [new MeasureFieldDto { FieldName = "Region", Aggregation = AggregationType.Count }],
                }, (string?)null));

            await new NlQueryPlanner(NullLogger<NlQueryPlanner>.Instance, openAI.Object, clock).BuildPlanAsync(
                [("Region", "String")], "count by region", "RULES", Mock.Of<INlValueSource>(),
                _ => Task.FromResult<string?>(null), CancellationToken.None);

            Assert.Contains("\nTODAY: 2026-10-22", system);
        }
    }
}
