using System.Reflection;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// Which endpoints charge, and for which feature (its cost is in <see cref="UsageCosts.Of"/>).
    /// Charging lives on the endpoint (<see cref="UsageLimitAttribute"/>), not inside the services,
    /// so this is the one place to check it — including the endpoints that must stay free.
    /// </summary>
    public class UsageLimitEndpointTests
    {
        private static UsageLimitAttribute? UsageOf(Type controller, string action) =>
            controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
                .GetCustomAttribute<UsageLimitAttribute>();

        public static TheoryData<string, UsageFeature> ChargedExamEndpoints => new()
        {
            { nameof(ExamController.GetClozeTest), UsageFeature.ClozeTest },
            { nameof(ExamController.GetTranslationTest), UsageFeature.TranslationTest },
            { nameof(ExamController.GetTranslationQualityGrading), UsageFeature.TranslationGrading },
            { nameof(ExamController.StartConversationTest), UsageFeature.ConversationStart },
            { nameof(ExamController.RespondToConversation), UsageFeature.ConversationResponse },
            { nameof(ExamController.SummarizeConversation), UsageFeature.ConversationSummary },
            { nameof(ExamController.TranscribeAudio), UsageFeature.AudioTranscription },
            { nameof(ExamController.GetSentenceTest), UsageFeature.SentenceTest },
            { nameof(ExamController.EvaluateSentence), UsageFeature.SentenceEvaluation },
            { nameof(ExamController.GetListeningTest), UsageFeature.ListeningTest },
            { nameof(ExamController.GetListeningResponseTest), UsageFeature.ListeningResponseTest },
            { nameof(ExamController.GetReadingTest), UsageFeature.ReadingComprehensionTest },
            { nameof(ExamController.EvaluateWriting), UsageFeature.WritingEvaluation },
            { nameof(ExamController.GetDictationTest), UsageFeature.DictationTest },
        };

        [Theory]
        [MemberData(nameof(ChargedExamEndpoints))]
        public void ExamEndpoint_Charges(string action, UsageFeature feature)
        {
            Assert.Equal(feature, UsageOf(typeof(ExamController), action)?.Feature);
        }

        /// <summary>No AI cost of their own: the writing prompt and dictation grading are plain DB
        /// work, recording a result is bookkeeping, and the audio is covered by the question's charge.</summary>
        [Theory]
        [InlineData(nameof(ExamController.GetWritingPrompt))]
        [InlineData(nameof(ExamController.GradeDictation))]
        [InlineData(nameof(ExamController.RecordExamResult))]
        [InlineData(nameof(ExamController.GetListeningOptionAudio))]
        [InlineData(nameof(ExamController.GetListeningResponsePromptAudio))]
        [InlineData(nameof(ExamController.GetListeningResponseOptionAudio))]
        [InlineData(nameof(ExamController.GetDictationWordAudio))]
        public void ExamEndpoint_IsFree(string action)
        {
            Assert.Null(UsageOf(typeof(ExamController), action));
        }

        [Fact]
        public void PortfolioAiSummary_Charges()
        {
            Assert.Equal(UsageFeature.PortfolioAiSummary,
                UsageOf(typeof(PortfolioController), nameof(PortfolioController.GetAiTechSummary))?.Feature);
        }

        [Fact]
        public void StockAnalysis_Charges()
        {
            Assert.Equal(UsageFeature.StockAnalysis,
                UsageOf(typeof(FinanceController), nameof(FinanceController.AnalyzeStock))?.Feature);
        }

        [Fact]
        public void DataAnalysisNlSummarize_Charges()
        {
            Assert.Equal(UsageFeature.NlSummarize,
                UsageOf(typeof(DataAnalysisController), nameof(DataAnalysisController.NlSummarize))?.Feature);
        }
    }
}
