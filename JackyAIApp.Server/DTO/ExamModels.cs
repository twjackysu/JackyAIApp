namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// Cloze test (fill-in-the-blank)
    /// </summary>
    public class ClozeTest
    {
        /// <summary>
        /// Gets or sets the question text for the cloze test.
        /// </summary>
        public required string Question { get; set; }

        /// <summary>
        /// Gets or sets the list of options available for the cloze test.
        /// </summary>
        public required List<string> Options { get; set; }

        /// <summary>
        /// Gets or sets the correct answer for the cloze test.
        /// </summary>
        public required string Answer { get; set; }
    }

    /// <summary>
    /// Cloze test response including the target word id so the client can report the result.
    /// </summary>
    public class ClozeTestResponse : ClozeTest
    {
        public required string WordId { get; set; }
    }

    /// <summary>
    /// Translation test
    /// </summary>
    public class TranslationTest
    {
        public required string Chinese { get; set; }
        public required string English { get; set; }
    }

    /// <summary>
    /// Translation test response including the target word
    /// </summary>
    public class TranslationTestResponse : TranslationTest
    {
        public required string Word { get; set; }
        public required string WordId { get; set; }
    }

    /// <summary>
    /// User's response to a translation test
    /// </summary>
    public class TranslationTestUserResponse
    {
        /// <summary>
        /// Unfamiliar word
        /// </summary>
        public required string UnfamiliarWords { get; set; }

        /// <summary>
        /// Sentence to be translated (Traditional Chinese)
        /// </summary>
        public required string ExaminationQuestion { get; set; }

        /// <summary>
        /// User's English translation
        /// </summary>
        public required string Translation { get; set; }
    }

    /// <summary>
    /// AI grading response for translation quality
    /// </summary>
    public class TranslationQualityGradingAssistantResponse
    {
        /// <summary>One of "A", "B", "C", "D", "E".</summary>
        public required string Grade { get; set; }

        /// <summary>Short explanation in Traditional Chinese.</summary>
        public required string Reason { get; set; }

        /// <summary>Corrected/natural English version of the sentence.</summary>
        public required string CorrectedTranslation { get; set; }

        /// <summary>Whether the translation correctly used the target unfamiliar word.</summary>
        public required bool WordUsed { get; set; }
    }

    /// <summary>
    /// Sentence formation test
    /// </summary>
    public class SentenceTest
    {
        public required string Prompt { get; set; }
        public required string SampleAnswer { get; set; }
        public required string Context { get; set; }
        public required int DifficultyLevel { get; set; }
        public string? GrammarPattern { get; set; }
    }

    /// <summary>
    /// Sentence test response including the target word
    /// </summary>
    public class SentenceTestResponse : SentenceTest
    {
        public required string Word { get; set; }
        public required string WordId { get; set; }
    }

    /// <summary>
    /// User's response to a sentence test
    /// </summary>
    public class SentenceTestUserResponse
    {
        public required string Word { get; set; }
        public required string Prompt { get; set; }
        public required string Context { get; set; }
        public required string UserSentence { get; set; }
        public required int DifficultyLevel { get; set; }
        public string? GrammarPattern { get; set; }
    }

    /// <summary>
    /// AI grading response for sentence quality
    /// </summary>
    public class SentenceTestGradingResponse
    {
        public required int Score { get; set; }
        public required string GrammarFeedback { get; set; }
        public required string UsageFeedback { get; set; }
        public required string CreativityFeedback { get; set; }
        public required string OverallFeedback { get; set; }
        public required List<string> Suggestions { get; set; }
    }

    /// <summary>
    /// Client-reported exam outcome. Feeds the word's spaced-repetition schedule
    /// and the per-word exam history. No credits charged (generation/grading already were).
    /// </summary>
    public class RecordExamResultRequest
    {
        /// <summary>Required for word-based exam types; omitted for "Listening"/"ListeningResponse".</summary>
        public string? WordId { get; set; }
        /// <summary>Required when ExamType is "Listening"; omitted otherwise.</summary>
        public string? ListeningTestId { get; set; }
        /// <summary>Required when ExamType is "ListeningResponse"; omitted otherwise.</summary>
        public string? ListeningResponseTestId { get; set; }
        /// <summary>Required when ExamType is "Reading"; omitted otherwise.</summary>
        public string? ReadingPassageId { get; set; }
        /// <summary>
        /// UserWord ids the writing prompt was built around, for the SM-2 fan-out. Required when
        /// ExamType is "Writing" and the prompt actually had required words; a zero-required-words
        /// open-topic submission legitimately omits/empties this.
        /// </summary>
        public List<string>? WritingWordIds { get; set; }
        /// <summary>"Cloze" | "Translation" | "Sentence" | "Listening" | "ListeningResponse" | "Reading" | "Writing" | "Dictation".</summary>
        public required string ExamType { get; set; }
        public required bool Correct { get; set; }
        /// <summary>Optional 0–100 score for graded types (Sentence, Writing).</summary>
        public int? Score { get; set; }
    }

    /// <summary>Result of recording an exam attempt.</summary>
    public class RecordExamResultResponse
    {
        /// <summary>True if the word is in the user's repository and its schedule was updated.</summary>
        public required bool ScheduleUpdated { get; set; }
        /// <summary>The word's next review date after applying the result (null if not scheduled).</summary>
        public DateTime? NextReviewDate { get; set; }
    }

    /// <summary>
    /// TOEIC Part 1 style "look at the picture, pick the best description" question. IsCorrect
    /// and Explanation are sent up front (like SentenceTest.SampleAnswer) — the client gates
    /// their display in the UI until the user has answered, rather than needing a second
    /// round-trip to reveal them.
    /// </summary>
    public class ListeningTestResponse
    {
        public required string Id { get; set; }
        public required string ImageUrl { get; set; }
        public required List<ListeningTestOptionDto> Options { get; set; }
    }

    public class ListeningTestOptionDto
    {
        public required string Id { get; set; }
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>Raw shape the AI returns for a new listening question — no Ids yet, those are
    /// assigned when persisting. Used by the admin "generate" flow only.</summary>
    public class ListeningQuestionGenerationResult
    {
        /// <summary>English image-generation prompt describing a single, unambiguous scene.</summary>
        public required string ImagePrompt { get; set; }
        public required List<ListeningQuestionOptionGen> Options { get; set; }
    }

    public class ListeningQuestionOptionGen
    {
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>Admin-only paginated view of the listening question bank.</summary>
    public class ListeningBankPageResponse
    {
        public required List<ListeningTestResponse> Items { get; set; }
        public required int TotalCount { get; set; }
    }

    /// <summary>
    /// TOEIC Part 2 style "hear a question/statement, pick the best spoken response" question.
    /// IsCorrect/Explanation sent up front, same UI-gating convention as ListeningTestResponse.
    /// </summary>
    public class ListeningResponseTestResponse
    {
        public required string Id { get; set; }
        public required string Prompt { get; set; }
        public required List<ListeningResponseOptionDto> Options { get; set; }
    }

    public class ListeningResponseOptionDto
    {
        public required string Id { get; set; }
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>Raw shape the AI returns for a new Part 2 question — no Ids yet.</summary>
    public class ListeningResponseGenerationResult
    {
        public required string Prompt { get; set; }
        public required List<ListeningResponseOptionGen> Options { get; set; }
    }

    public class ListeningResponseOptionGen
    {
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>
    /// Passage-level reading comprehension exercise: a short passage plus 3–4 multiple-choice
    /// comprehension questions. IsCorrect/Explanation sent up front, same UI-gating convention
    /// as ListeningResponseTestResponse.
    /// </summary>
    public class ReadingPassageResponse
    {
        public required string Id { get; set; }
        public required string Title { get; set; }
        public required string PassageText { get; set; }
        public required List<ReadingQuestionDto> Questions { get; set; }
    }

    public class ReadingQuestionDto
    {
        public required string Id { get; set; }
        public required string QuestionText { get; set; }
        public required List<ReadingQuestionOptionDto> Options { get; set; }
    }

    public class ReadingQuestionOptionDto
    {
        public required string Id { get; set; }
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>Raw shape the AI returns for a new reading passage — no Ids yet, those are
    /// assigned when persisting.</summary>
    public class ReadingGenerationResult
    {
        public required string Title { get; set; }
        public required string PassageText { get; set; }
        public required List<ReadingQuestionGen> Questions { get; set; }
    }

    public class ReadingQuestionGen
    {
        public required string QuestionText { get; set; }
        public required List<ReadingQuestionOptionGen> Options { get; set; }
    }

    public class ReadingQuestionOptionGen
    {
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
        public required string Explanation { get; set; }
    }

    /// <summary>
    /// Open-ended writing prompt: a topic plus up to 5 of the calling user's own saved words to
    /// work into a paragraph. Free to produce (plain DB queries, no AI call) — see
    /// ExamService.GenerateWritingPromptAsync.
    /// </summary>
    public class WritingPromptResponse
    {
        public required string Topic { get; set; }
        public required List<WritingPromptWordDto> RequiredWords { get; set; }
    }

    public class WritingPromptWordDto
    {
        public required string WordId { get; set; }
        public required string WordText { get; set; }
    }

    /// <summary>
    /// User's writing submission. Topic/RequiredWords are echoed back by the client from the
    /// GET api/exam/writing response rather than re-queried, same pattern as
    /// TranslationTestUserResponse.ExaminationQuestion.
    /// </summary>
    public class WritingSubmissionRequest
    {
        public required string Topic { get; set; }
        public required List<string> RequiredWords { get; set; }
        public required string Submission { get; set; }
    }

    /// <summary>
    /// AI grading response for an open-ended writing submission.
    /// </summary>
    public class WritingGradingResponse
    {
        public required int Score { get; set; }
        public required string GrammarFeedback { get; set; }
        /// <summary>Topic relevance and structure/coherence.</summary>
        public required string ContentFeedback { get; set; }
        /// <summary>Overall comment on how the required words were used (or not).</summary>
        public required string VocabularyFeedback { get; set; }
        public required List<WritingWordUsageDto> WordsUsed { get; set; }
        public required List<string> Suggestions { get; set; }
    }

    public class WritingWordUsageDto
    {
        public required string Word { get; set; }
        public required bool Used { get; set; }
    }

    /// <summary>
    /// Dictation prompt: only the picked word's id. Deliberately omits the word text — unlike
    /// Cloze/Translation, which already put the answer in their GET response, dictation's whole
    /// point is "identify the word from audio alone," so the text must not be inspectable in the
    /// network response before grading. See ExamService.GenerateDictationExamAsync.
    /// </summary>
    public class DictationPromptResponse
    {
        public required string WordId { get; set; }
    }

    /// <summary>User's dictation submission for a given word.</summary>
    public class DictationGradeRequest
    {
        public required string WordId { get; set; }
        public required string UserInput { get; set; }
    }

    /// <summary>
    /// Dictation grading result. CorrectText reveals the answer for the post-submission UI.
    /// </summary>
    public class DictationGradeResponse
    {
        public required string CorrectText { get; set; }
        public required bool IsCorrect { get; set; }
        /// <summary>0-100 closeness score. Purely informational — does not affect IsCorrect.</summary>
        public required int Score { get; set; }
    }
}
