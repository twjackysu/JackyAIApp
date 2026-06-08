using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Exam
{
    /// <summary>
    /// Exam domain operations: generating vocabulary-based tests (cloze / translation /
    /// sentence), grading, and the conversation/audio practice flows.
    /// </summary>
    public interface IExamService
    {
        /// <summary>Generates (or returns a cached) vocabulary-based exam for the current user.</summary>
        Task<ServiceResult> GenerateWordExamAsync(ExamType type);

        Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ServiceResult> StartConversationAsync(ConversationStartRequest request);

        Task<ServiceResult> RespondToConversationAsync(ConversationResponseRequest request);

        Task<ServiceResult> TranscribeAudioAsync(Stream audioStream, string fileName);

        Task<ServiceResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);
    }
}
