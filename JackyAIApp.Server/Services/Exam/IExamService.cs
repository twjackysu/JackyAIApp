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

        /// <summary>
        /// Serves a random TOEIC Part 1 style listening question from the pre-seeded bank (see
        /// Tools/ListeningBankImporter — this isn't word-scoped, so it doesn't go through
        /// GenerateWordExamAsync/IWordExamGenerator).
        /// </summary>
        Task<ServiceResult> GenerateListeningExamAsync();

        /// <summary>Looks up a listening question option's spoken text, for the unbilled audio
        /// playback endpoint (credits are already charged when the question itself was fetched).</summary>
        Task<ServiceResult> GetListeningOptionTextAsync(string optionId);

        /// <summary>Admin-only: paginated view of the full listening question bank.</summary>
        Task<ServiceResult> GetListeningBankAsync(int pageNumber, int pageSize);

        Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ServiceResult> StartConversationAsync(ConversationStartRequest request);

        Task<ServiceResult> RespondToConversationAsync(ConversationResponseRequest request);

        /// <summary>Produces an end-of-session recap (corrections, better phrasings, word suggestions).</summary>
        Task<ServiceResult> SummarizeConversationAsync(ConversationSummaryRequest request);

        Task<ServiceResult> TranscribeAudioAsync(Stream audioStream, string fileName);

        Task<ServiceResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);

        /// <summary>
        /// Records an exam outcome: logs an <see cref="Data.Models.SQL.ExamAttempt"/> and feeds the
        /// word's spaced-repetition schedule (wrong → relearn soon, right → mild boost). Free.
        /// </summary>
        Task<ServiceResult> RecordExamResultAsync(RecordExamResultRequest request);
    }
}
