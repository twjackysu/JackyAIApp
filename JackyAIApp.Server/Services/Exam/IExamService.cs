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
        Task<ExamResult> GenerateWordExamAsync(ExamType type);

        Task<ExamResult> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ExamResult> StartConversationAsync(ConversationStartRequest request);

        Task<ExamResult> RespondToConversationAsync(ConversationResponseRequest request);

        Task<ExamResult> TranscribeAudioAsync(Stream audioStream, string fileName);

        Task<ExamResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);
    }
}
