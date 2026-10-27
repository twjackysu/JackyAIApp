using System.Reflection;
using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Exam;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Tests.Services.Exam
{
    /// <summary>What an AI-graded exam sends to the model is bounded (D-165): the JSON the prompt carries, and the request body.</summary>
    public class ExamInputLimitsTests
    {
        [Fact]
        public void AnAnswerAsThePagesSendIt_Passes_AndAHugeOne_IsRefused()
        {
            var ordinary = new WritingSubmissionRequest
            {
                Topic = "a memorable experience you had",
                RequiredWords = ["adventure", "journey"],
                Submission = string.Join(' ', Enumerable.Repeat("I had an unexpected adventure on my journey.", 60)),
            };
            Assert.Null(ExamInputLimits.TooLong(ordinary, ExamInputLimits.MaxWritingChars));

            ordinary.Submission = new string('a', ExamInputLimits.MaxWritingChars);
            var refused = ExamInputLimits.TooLong(ordinary, ExamInputLimits.MaxWritingChars);
            Assert.NotNull(refused);
            Assert.Equal(ExamInputLimits.TooLongMessage, refused.Value.Message);
        }

        [Theory]
        [InlineData(typeof(WordExamController), nameof(WordExamController.GetTranslationQualityGrading))]
        [InlineData(typeof(WordExamController), nameof(WordExamController.EvaluateSentence))]
        [InlineData(typeof(WritingExamController), nameof(WritingExamController.EvaluateWriting))]
        [InlineData(typeof(ConversationPracticeController), nameof(ConversationPracticeController.StartConversationTest))]
        [InlineData(typeof(ConversationPracticeController), nameof(ConversationPracticeController.RespondToConversation))]
        [InlineData(typeof(ConversationPracticeController), nameof(ConversationPracticeController.SummarizeConversation))]
        [InlineData(typeof(ExamResultController), nameof(ExamResultController.RecordExamResult))]
        public void EveryGradingEndpoint_HoldsItsRequestBody(Type controller, string action)
        {
            // Without it Kestrel reads about 28 MB before the action can refuse.
            Assert.NotNull(controller.GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>());
        }
    }
}
