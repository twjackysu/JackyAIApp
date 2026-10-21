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
        Task<ServiceResult<object>> GenerateWordExamAsync(ExamType type);

        /// <summary>
        /// Serves a random TOEIC Part 1 style listening question from the pre-seeded bank (see
        /// Tools/ListeningBankImporter — this isn't word-scoped, so it doesn't go through
        /// GenerateWordExamAsync/IWordExamGenerator).
        /// </summary>
        Task<ServiceResult<ListeningTestResponse>> GenerateListeningExamAsync();

        /// <summary>Looks up a listening question option's spoken text, for the unbilled audio
        /// playback endpoint (usage was already charged when the question itself was fetched).</summary>
        Task<ServiceResult<string>> GetListeningOptionTextAsync(string optionId);

        /// <summary>Admin-only: paginated view of the full listening question bank.</summary>
        Task<ServiceResult<ListeningBankPageResponse>> GetListeningBankAsync(int pageNumber, int pageSize);

        /// <summary>
        /// Serves a TOEIC Part 2 style "question/statement + 3 spoken responses" question. No
        /// image, so — unlike Part 1 — generated live on demand and persisted for reuse (like
        /// Cloze/Translation/Sentence): while the bank is small, always generates a fresh one via
        /// AI; once it has grown, mostly reuses a random existing entry with an occasional
        /// AI-generated top-up.
        /// </summary>
        Task<ServiceResult<ListeningResponseTestResponse>> GenerateListeningResponseExamAsync();

        /// <summary>Looks up a Part 2 question's spoken prompt text, for the unbilled audio
        /// playback endpoint.</summary>
        Task<ServiceResult<string>> GetListeningResponsePromptTextAsync(string testId);

        /// <summary>Looks up a Part 2 response option's spoken text, for the unbilled audio
        /// playback endpoint.</summary>
        Task<ServiceResult<string>> GetListeningResponseOptionTextAsync(string optionId);

        /// <summary>
        /// Serves a passage-level reading comprehension exercise (short passage + 3-4 MCQ
        /// comprehension questions). Not word-scoped, shared bank — generated live on demand and
        /// persisted for reuse, same growth-threshold pattern as
        /// <see cref="GenerateListeningResponseExamAsync"/>.
        /// </summary>
        Task<ServiceResult<ReadingPassageResponse>> GenerateReadingExamAsync();

        /// <summary>
        /// Serves an open-ended writing prompt: a topic from a fixed rotation plus up to 5 of the
        /// calling user's own saved words (empty if the user has none — open-topic writing, not an
        /// error). Both plain DB queries, no AI call, so unlike every other exam-type generation
        /// endpoint this is deliberately not usage-limit-gated.
        /// </summary>
        Task<ServiceResult<WritingPromptResponse>> GenerateWritingPromptAsync();

        /// <summary>
        /// Grades an open-ended writing submission against its topic/required words — same one-shot
        /// grading shape as <see cref="GradeTranslationAsync"/>/<see cref="EvaluateSentenceAsync"/>.
        /// </summary>
        Task<ServiceResult<WritingGradingResponse>> GradeWritingAsync(WritingSubmissionRequest request);

        /// <summary>
        /// Picks one word (reusing the same due/weak/global-fallback logic as
        /// <see cref="GenerateWordExamAsync"/> via the shared word-picking helper) for dictation
        /// practice. Returns only the WordId — unlike Cloze/Translation, the word text is
        /// deliberately withheld since the whole exercise is identifying it from audio alone.
        /// </summary>
        Task<ServiceResult<DictationPromptResponse>> GenerateDictationExamAsync();

        /// <summary>Looks up a dictation word's text, for the unbilled audio playback endpoint
        /// (usage was already charged when the prompt itself was fetched).</summary>
        Task<ServiceResult<string>> GetDictationWordTextAsync(string wordId);

        /// <summary>
        /// Grades a dictation submission against the real word text looked up server-side (never
        /// trust a client-supplied answer). Zero underlying cost (in-memory string comparison),
        /// so deliberately not usage-limit-gated.
        /// </summary>
        Task<ServiceResult<DictationGradeResponse>> GradeDictationAsync(DictationGradeRequest request);

        Task<ServiceResult<TranslationQualityGradingAssistantResponse>> GradeTranslationAsync(TranslationTestUserResponse userResponse);

        Task<ServiceResult<ConversationStartResponse>> StartConversationAsync(ConversationStartRequest request);

        Task<ServiceResult<ConversationResponseResponse>> RespondToConversationAsync(ConversationResponseRequest request);

        /// <summary>Produces an end-of-session recap (corrections, better phrasings, word suggestions).</summary>
        Task<ServiceResult<ConversationSummaryResponse>> SummarizeConversationAsync(ConversationSummaryRequest request);

        Task<ServiceResult<WhisperTranscriptionResponse>> TranscribeAudioAsync(Stream audioStream, string fileName);

        Task<ServiceResult<SentenceTestGradingResponse>> EvaluateSentenceAsync(SentenceTestUserResponse userResponse);

        /// <summary>
        /// Records an exam outcome: logs an <see cref="Data.Models.SQL.ExamAttempt"/> and feeds the
        /// word's spaced-repetition schedule (wrong → relearn soon, right → mild boost). Free.
        /// </summary>
        Task<ServiceResult<RecordExamResultResponse>> RecordExamResultAsync(RecordExamResultRequest request);
    }
}
