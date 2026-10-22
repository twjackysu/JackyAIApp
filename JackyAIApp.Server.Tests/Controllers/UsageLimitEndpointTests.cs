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
            { typeof(WordExamController), nameof(WordExamController.GetClozeTest), UsageFeature.ClozeTest },
            { typeof(WordExamController), nameof(WordExamController.GetTranslationTest), UsageFeature.TranslationTest },
            { typeof(WordExamController), nameof(WordExamController.GetTranslationQualityGrading), UsageFeature.TranslationGrading },
            { typeof(ConversationPracticeController), nameof(ConversationPracticeController.StartConversationTest), UsageFeature.ConversationStart },
            { typeof(ConversationPracticeController), nameof(ConversationPracticeController.RespondToConversation), UsageFeature.ConversationResponse },
            { typeof(ConversationPracticeController), nameof(ConversationPracticeController.SummarizeConversation), UsageFeature.ConversationSummary },
            { typeof(ConversationPracticeController), nameof(ConversationPracticeController.TranscribeAudio), UsageFeature.AudioTranscription },
            { typeof(WordExamController), nameof(WordExamController.GetSentenceTest), UsageFeature.SentenceTest },
            { typeof(WordExamController), nameof(WordExamController.EvaluateSentence), UsageFeature.SentenceEvaluation },
            { typeof(ListeningExamController), nameof(ListeningExamController.GetListeningTest), UsageFeature.ListeningTest },
            { typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponseTest), UsageFeature.ListeningResponseTest },
            { typeof(ReadingExamController), nameof(ReadingExamController.GetReadingTest), UsageFeature.ReadingComprehensionTest },
            { typeof(WritingExamController), nameof(WritingExamController.EvaluateWriting), UsageFeature.WritingEvaluation },
            { typeof(DictationExamController), nameof(DictationExamController.GetDictationTest), UsageFeature.DictationTest },
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
        [InlineData(typeof(WritingExamController), nameof(WritingExamController.GetWritingPrompt))]
        [InlineData(typeof(DictationExamController), nameof(DictationExamController.GradeDictation))]
        [InlineData(typeof(ExamResultController), nameof(ExamResultController.RecordExamResult))]
        [InlineData(typeof(ListeningExamController), nameof(ListeningExamController.GetListeningOptionAudio))]
        [InlineData(typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponsePromptAudio))]
        [InlineData(typeof(ListeningResponseExamController), nameof(ListeningResponseExamController.GetListeningResponseOptionAudio))]
        [InlineData(typeof(DictationExamController), nameof(DictationExamController.GetDictationWordAudio))]
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
