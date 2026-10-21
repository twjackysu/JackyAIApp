using System.Reflection;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// Which endpoints charge, and how much. Charging lives on the endpoint (<see cref="UsageLimitAttribute"/>),
    /// not inside the services, so this is the one place to check it — including the endpoints that
    /// must stay free.
    /// </summary>
    public class UsageLimitEndpointTests
    {
        private static UsageLimitAttribute? UsageOf(Type controller, string action) =>
            controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
                .GetCustomAttribute<UsageLimitAttribute>();

        public static TheoryData<string, ulong> ChargedExamEndpoints => new()
        {
            { nameof(ExamController.GetClozeTest), UsageCosts.ClozeTest },
            { nameof(ExamController.GetTranslationTest), UsageCosts.TranslationTest },
            { nameof(ExamController.GetTranslationQualityGrading), UsageCosts.TranslationGrading },
            { nameof(ExamController.StartConversationTest), UsageCosts.ConversationStart },
            { nameof(ExamController.RespondToConversation), UsageCosts.ConversationResponse },
            { nameof(ExamController.SummarizeConversation), UsageCosts.ConversationSummary },
            { nameof(ExamController.TranscribeAudio), UsageCosts.AudioTranscription },
            { nameof(ExamController.GetSentenceTest), UsageCosts.SentenceTest },
            { nameof(ExamController.EvaluateSentence), UsageCosts.SentenceEvaluation },
            { nameof(ExamController.GetListeningTest), UsageCosts.ListeningTest },
            { nameof(ExamController.GetListeningResponseTest), UsageCosts.ListeningResponseTest },
            { nameof(ExamController.GetReadingTest), UsageCosts.ReadingComprehensionTest },
            { nameof(ExamController.EvaluateWriting), UsageCosts.WritingEvaluation },
            { nameof(ExamController.GetDictationTest), UsageCosts.DictationTest },
        };

        [Theory]
        [MemberData(nameof(ChargedExamEndpoints))]
        public void ExamEndpoint_Charges(string action, ulong cost)
        {
            Assert.Equal(cost, UsageOf(typeof(ExamController), action)?.Cost);
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
            Assert.Equal(UsageCosts.PortfolioAiSummary,
                UsageOf(typeof(PortfolioController), nameof(PortfolioController.GetAiTechSummary))?.Cost);
        }

        [Fact]
        public void DataAnalysisNlSummarize_Charges()
        {
            Assert.Equal(UsageCosts.NlSummarize,
                UsageOf(typeof(DataAnalysisController), nameof(DataAnalysisController.NlSummarize))?.Cost);
        }
    }
}
