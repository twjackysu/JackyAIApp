using System.Text.Json;
using DataAnalysisLibrary.Core.Enums;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.DataAnalysis;

namespace JackyAIApp.Server.Tests.Common
{
    /// <summary>
    /// The prompts and the models' answers were written against Newtonsoft.Json. AiJson is
    /// System.Text.Json set up to read every answer Newtonsoft read and to write the same prompts,
    /// so moving off Newtonsoft does not turn a working answer into "Failed to parse".
    /// </summary>
    public class AiJsonTests
    {
        [Fact]
        public void PropertyNamesMatchInAnyCase()
        {
            var grading = AiJson.Deserialize<SentenceTestGradingResponse>(
                """{"score":8,"grammarFeedback":"g","USAGEFEEDBACK":"u","CreativityFeedback":"c","overallFeedback":"o","suggestions":[]}""")!;

            Assert.Equal(8, grading.Score);
            Assert.Equal("g", grading.GrammarFeedback);
            Assert.Equal("u", grading.UsageFeedback);
        }

        [Fact]
        public void ARequiredFieldTheModelLeftOut_IsNull_NotAFailure()
        {
            var word = AiJson.Deserialize<WordBase>("""{"Word":"set","KKPhonics":"[sɛt]"}""")!;

            Assert.Equal("set", word.Word);
            Assert.Null(word.Meanings);
        }

        [Fact]
        public void ValuesInTheWrongJsonType_AreReadAsNewtonsoftDid()
        {
            var grading = AiJson.Deserialize<SentenceTestGradingResponse>(
                """{"Score":"7","GrammarFeedback":1,"UsageFeedback":true,"CreativityFeedback":"c","OverallFeedback":"o","Suggestions":[2024]}""")!;
            var correction = AiJson.Deserialize<ConversationResponseResponse>(
                """{"AiResponse":"hi","Correction":{"HasCorrection":"false"}}""")!;

            Assert.Equal(7, grading.Score);
            Assert.Equal("1", grading.GrammarFeedback);
            Assert.Equal("true", grading.UsageFeedback);
            Assert.Equal(["2024"], grading.Suggestions);
            Assert.False(correction.Correction.HasCorrection);
        }

        [Fact]
        public void EnumsByName_AndFilterValuesWrittenAsNumbers()
        {
            var plan = AiJson.Deserialize<NlPlanDto>("""
                {"viewBy":[{"type":"Plain","fieldName":"Region"}],
                 "measures":[{"fieldName":"Revenue","aggregation":"average"}],
                 "filters":[{"fieldName":"Year","values":[2024, 2025],"op":"In"}],}
                """)!;

            Assert.Equal(AggregationType.Average, Assert.Single(plan.Measures).Aggregation);
            Assert.Equal(["2024", "2025"], Assert.Single(plan.Filters!).Values);
        }

        [Fact]
        public void ADateNotInIso8601_IsRead()
        {
            var analysis = AiJson.Deserialize<StockTrendAnalysis>(
                """{"StockCode":"2330", /* the model's own note */ "LastUpdated":"2026-10-22 10:30:00"}""")!;

            Assert.Equal(new DateTime(2026, 10, 22, 10, 30, 0), analysis.LastUpdated);
        }

        [Fact]
        public void APriceInQuotesWithAThousandsSeparator_IsRead()
        {
            var analysis = AiJson.Deserialize<StockTrendAnalysis>("""{"CurrentPrice":"1,050.5"}""")!;
            var delivery = AiJson.Deserialize<DeliveryFeedback>("""{"PaceWpm":"1,20","PauseCount":2}""")!;

            Assert.Equal(1050.5m, analysis.CurrentPrice);
            Assert.Equal(120d, delivery.PaceWpm);
        }

        [Fact]
        public void WhatNewtonsoftRejected_IsStillRejected()
        {
            Assert.Throws<JsonException>(() => AiJson.Deserialize<SentenceTestGradingResponse>("""{"GrammarFeedback":{"a":1}}"""));
            Assert.Throws<JsonException>(() => AiJson.Deserialize<StockTrendAnalysis>("""{"LastUpdated":"current_timestamp"}"""));
        }

        [Fact]
        public void WritesThePromptsAsBefore_CSharpNames_NullsKept_ChineseUnescaped()
        {
            var json = AiJson.Serialize(new TranslationTest { Chinese = "她將書放在桌上。", English = "She set the \"book\" down." });
            var withNull = AiJson.Serialize(new SentenceTest { Prompt = "p", SampleAnswer = "s", Context = "c", DifficultyLevel = 2 });

            Assert.Equal("""{"Chinese":"她將書放在桌上。","English":"She set the \"book\" down."}""", json);
            Assert.Contains("\"GrammarPattern\":null", withNull);
            Assert.Contains("\"DifficultyLevel\":2", withNull);
        }

        [Fact]
        public void TheServerNoLongerReferencesNewtonsoft()
        {
            // It still arrives transitively (DotnetSdkUtilities, Kusto, EF tooling), so nothing stops
            // a `using Newtonsoft.Json;` from compiling. This does.
            Assert.DoesNotContain(
                typeof(AiJson).Assembly.GetReferencedAssemblies(),
                a => a.Name == "Newtonsoft.Json");
        }
    }
}
