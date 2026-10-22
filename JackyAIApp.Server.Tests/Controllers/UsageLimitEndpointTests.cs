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

        public static TheoryData<Type, string, UsageFeature> ChargedExamEndpoints => new()
        {
            { typeof(ExamController), nameof(ExamController.GetClozeTest), UsageFeature.ClozeTest },
            { typeof(ExamController), nameof(ExamController.GetTranslationTest), UsageFeature.TranslationTest },
            { typeof(ExamController), nameof(ExamController.GetTranslationQualityGrading), UsageFeature.TranslationGrading },
            { typeof(ExamController), nameof(ExamController.StartConversationTest), UsageFeature.ConversationStart },
            { typeof(ExamController), nameof(ExamController.RespondToConversation), UsageFeature.ConversationResponse },
            { typeof(ExamController), nameof(ExamController.SummarizeConversation), UsageFeature.ConversationSummary },
            { typeof(ExamController), nameof(ExamController.TranscribeAudio), UsageFeature.AudioTranscription },
            { typeof(ExamController), nameof(ExamController.GetSentenceTest), UsageFeature.SentenceTest },
            { typeof(ExamController), nameof(ExamController.EvaluateSentence), UsageFeature.SentenceEvaluation },
            { typeof(ListeningExamController), nameof(ListeningExamController.GetListeningTest), UsageFeature.ListeningTest },
            { typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponseTest), UsageFeature.ListeningResponseTest },
            { typeof(ReadingExamController), nameof(ReadingExamController.GetReadingTest), UsageFeature.ReadingComprehensionTest },
            { typeof(ExamController), nameof(ExamController.EvaluateWriting), UsageFeature.WritingEvaluation },
            { typeof(ExamController), nameof(ExamController.GetDictationTest), UsageFeature.DictationTest },
        };

        [Theory]
        [MemberData(nameof(ChargedExamEndpoints))]
        public void ExamEndpoint_Charges(Type controller, string action, UsageFeature feature)
        {
            Assert.Equal(feature, UsageOf(controller, action)?.Feature);
        }

        /// <summary>No AI cost of their own: the writing prompt and dictation grading are plain DB
        /// work, recording a result is bookkeeping, and the audio is covered by the question's charge.</summary>
        [Theory]
        [InlineData(typeof(ExamController), nameof(ExamController.GetWritingPrompt))]
        [InlineData(typeof(ExamController), nameof(ExamController.GradeDictation))]
        [InlineData(typeof(ExamController), nameof(ExamController.RecordExamResult))]
        [InlineData(typeof(ListeningExamController), nameof(ListeningExamController.GetListeningOptionAudio))]
        [InlineData(typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponsePromptAudio))]
        [InlineData(typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponseOptionAudio))]
        [InlineData(typeof(ExamController), nameof(ExamController.GetDictationWordAudio))]
        public void ExamEndpoint_IsFree(Type controller, string action)
        {
            Assert.Null(UsageOf(controller, action));
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
