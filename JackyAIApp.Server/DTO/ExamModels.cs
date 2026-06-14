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
        /// <summary>
        /// Translation quality grading based on the user's input, along with the reason
        /// </summary>
        public required string TranslationQualityGrading { get; set; }
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
        public required string WordId { get; set; }
        /// <summary>"Cloze" | "Translation" | "Sentence".</summary>
        public required string ExamType { get; set; }
        public required bool Correct { get; set; }
        /// <summary>Optional 0–100 score for graded types (Sentence).</summary>
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
}
