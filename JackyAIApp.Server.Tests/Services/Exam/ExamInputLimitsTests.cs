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

        private static ConversationResponseRequest LongConversation(int turns, string userMessage = "How about the dessert?") => new()
        {
            ConversationContext = new ConversationContext { Scenario = "restaurant", UserRole = "customer", AiRole = "waiter", TurnNumber = turns },
            ConversationHistory = [.. Enumerable.Range(0, turns).Select(i => new ConversationTurn
            {
                Speaker = i % 2 == 0 ? "ai" : "user",
                Message = $"turn {i}: " + new string('w', 300),
            })],
            UserMessage = userMessage,
        };

        [Fact]
        public void ALongConversation_ForgetsItsOldestTurns_ToFit()
        {
            // The page sends the whole history every turn: a long practice goes on, it is not refused.
            var request = LongConversation(300);
            Assert.NotNull(ExamInputLimits.TooLong(request, ExamInputLimits.MaxConversationChars));

            ExamInputLimits.TrimOldestTurns(request, request.ConversationHistory, ExamInputLimits.MaxConversationChars);

            Assert.Null(ExamInputLimits.TooLong(request, ExamInputLimits.MaxConversationChars));
            Assert.InRange(request.ConversationHistory.Count, 100, 299);
            Assert.StartsWith("turn 299:", request.ConversationHistory[^1].Message);
            // Only what had to go: one more turn would not have fitted.
            var oneMore = LongConversation(300);
            oneMore.ConversationHistory.RemoveRange(0, 300 - request.ConversationHistory.Count - 1);
            Assert.NotNull(ExamInputLimits.TooLong(oneMore, ExamInputLimits.MaxConversationChars));
        }

        [Fact]
        public void AShortConversation_IsLeftWhole_AndOneHugeMessage_StillTooLong()
        {
            var shortOne = LongConversation(10);
            ExamInputLimits.TrimOldestTurns(shortOne, shortOne.ConversationHistory, ExamInputLimits.MaxConversationChars);
            Assert.Equal(10, shortOne.ConversationHistory.Count);

            var huge = LongConversation(10, new string('x', ExamInputLimits.MaxConversationChars));
            ExamInputLimits.TrimOldestTurns(huge, huge.ConversationHistory, ExamInputLimits.MaxConversationChars);
            Assert.Empty(huge.ConversationHistory);
            Assert.NotNull(ExamInputLimits.TooLong(huge, ExamInputLimits.MaxConversationChars));
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
