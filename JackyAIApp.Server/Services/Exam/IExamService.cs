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

        /// <summary>
        /// Serves an open-ended writing prompt: a topic from a fixed rotation plus up to 5 of the
        /// calling user's own saved words (empty if the user has none — open-topic writing, not an
        /// error). Both plain DB queries, no AI call, so unlike every other exam-type generation
        /// endpoint this is deliberately not credit-gated.
        /// </summary>
        Task<ServiceResult> GenerateWritingPromptAsync();

        /// <summary>
        /// Grades an open-ended writing submission against its topic/required words — same one-shot
        /// grading shape as <see cref="GradeTranslationAsync"/>/<see cref="EvaluateSentenceAsync"/>.
        /// </summary>
        Task<ServiceResult> GradeWritingAsync(WritingSubmissionRequest request);

        Task<ServiceResult> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        /// <summary>
        /// Picks one of the user's words (same word-picking helper as Cloze/Translation/Sentence)
        /// and returns only its <c>WordId</c> — deliberately withholds the word text, since this
        /// exercise is "identify the word from audio alone."
        /// </summary>
        Task<ServiceResult> GenerateDictationExamAsync();

        /// <summary>Looks up a dictation word's text server-side, for the unbilled audio playback
        /// endpoint (credits are already charged when the prompt itself was fetched).</summary>
        Task<ServiceResult> GetDictationWordTextAsync(string wordId);

        /// <summary>
        /// Grades a dictation submission: looks up the real word text server-side and compares it
        /// (trimmed, case-insensitive exact match) against the user's input. Pure in-memory string
        /// comparison — no AI call, no TTS call — so deliberately not credit-gated.
        /// </summary>
        Task<ServiceResult> GradeDictationAsync(DictationGradeRequest request);

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
