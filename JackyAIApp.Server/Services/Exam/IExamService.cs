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

        /// <summary>
        /// Serves a TOEIC Part 2 style "question/statement + 3 spoken responses" question. No
        /// image, so — unlike Part 1 — generated live on demand and persisted for reuse (like
        /// Cloze/Translation/Sentence): while the bank is small, always generates a fresh one via
        /// AI; once it has grown, mostly reuses a random existing entry with an occasional
        /// AI-generated top-up.
        /// </summary>
        Task<ServiceResult> GenerateListeningResponseExamAsync();

        /// <summary>Looks up a Part 2 question's spoken prompt text, for the unbilled audio
        /// playback endpoint.</summary>
        Task<ServiceResult> GetListeningResponsePromptTextAsync(string testId);

        /// <summary>Looks up a Part 2 response option's spoken text, for the unbilled audio
        /// playback endpoint.</summary>
        Task<ServiceResult> GetListeningResponseOptionTextAsync(string optionId);

        /// <summary>
        /// Serves a passage-level reading comprehension exercise (short passage + 3-4 MCQ
        /// comprehension questions). Not word-scoped, shared bank — generated live on demand and
        /// persisted for reuse, same growth-threshold pattern as
        /// <see cref="GenerateListeningResponseExamAsync"/>.
        /// </summary>
        Task<ServiceResult> GenerateReadingExamAsync();

        Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ServiceResult> StartConversationAsync(ConversationStartRequest request);

        Task<ServiceResult> RespondToConversationAsync(ConversationResponseRequest request);

        /// <summary>Produces an end-of-session recap (corrections, better phrasings, word suggestions).</summary>
        Task<ServiceResult> SummarizeConversationAsync(ConversationSummaryRequest request);

        Task<ServiceResult> TranscribeAudioAsync(Stream audioStream, string fileName);

        Task<ServiceResult> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);

        /// <summary>
        /// Builds an open-ended writing prompt: a topic from a small fixed rotation plus up to 5
        /// distinct rows sampled from the calling user's own saved words (plain DB sample, no AI
        /// call — zero underlying cost). Works with an empty repository (empty RequiredWords).
        /// Deliberately NOT credit-gated.
        /// </summary>
        Task<ServiceResult> GenerateWritingPromptAsync();

        /// <summary>
        /// Grades an open-ended writing submission against the topic/required words the prompt
        /// gave, via one credit-gated AI call. Same decoupling as
        /// <see cref="GradeTranslationAsync"/>/<see cref="EvaluateSentenceAsync"/> — does not
        /// write an <see cref="Data.Models.SQL.ExamAttempt"/> itself.
        /// </summary>
        Task<ServiceResult> GradeWritingAsync(WritingSubmissionRequest request);

        /// <summary>
        /// Records an exam outcome: logs an <see cref="Data.Models.SQL.ExamAttempt"/> and feeds the
        /// word's spaced-repetition schedule (wrong → relearn soon, right → mild boost). Free.
        /// </summary>
        Task<ServiceResult> RecordExamResultAsync(RecordExamResultRequest request);
    }
}
