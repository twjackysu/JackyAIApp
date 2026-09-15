namespace JackyAIApp.Server.DTO
{
    /// <summary>
    /// Request to start a new conversation
    /// </summary>
    public class ConversationStartRequest
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public int DifficultyLevel { get; set; } = 3;
    }

    /// <summary>
    /// Response when starting a conversation
    /// </summary>
    public class ConversationStartResponse
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public required string Context { get; set; }
        public required string FirstMessage { get; set; }
    }

    /// <summary>
    /// Context for an ongoing conversation
    /// </summary>
    public class ConversationContext
    {
        public required string Scenario { get; set; }
        public required string UserRole { get; set; }
        public required string AiRole { get; set; }
        public required int TurnNumber { get; set; }
    }

    /// <summary>
    /// A single turn in a conversation
    /// </summary>
    public class ConversationTurn
    {
        public required string Speaker { get; set; }
        public required string Message { get; set; }
    }

    /// <summary>
    /// Request to continue a conversation
    /// </summary>
    public class ConversationResponseRequest
    {
        public required ConversationContext ConversationContext { get; set; }
        public required List<ConversationTurn> ConversationHistory { get; set; }
        public required string UserMessage { get; set; }
    }

    /// <summary>
    /// Correction suggestion for user's message
    /// </summary>
    public class ConversationCorrection
    {
        public required bool HasCorrection { get; set; }
        public string? OriginalText { get; set; }
        public string? SuggestedText { get; set; }
        public string? Explanation { get; set; }
    }

    /// <summary>
    /// AI response in a conversation
    /// </summary>
    public class ConversationResponseResponse
    {
        public required string AiResponse { get; set; }
        public required ConversationCorrection Correction { get; set; }
    }

    /// <summary>
    /// Whisper transcription response
    /// </summary>
    public class WhisperTranscriptionResponse
    {
        public required string Text { get; set; }

        /// <summary>
        /// Rule-based delivery/fluency feedback (pace, pauses, filler words) computed from the same
        /// Whisper call, at zero added AI/API cost. Null when the clip is too short/silent to derive
        /// meaningful metrics from — see <see cref="Services.Exam.SpeechFluencyAnalyzer"/>.
        /// This is delivery/fluency feedback, not phoneme-level pronunciation-accuracy scoring — do not
        /// rename/relabel it as "pronunciation" (see ARCHITECTURE.md, TASK-004).
        /// </summary>
        public DeliveryFeedback? DeliveryFeedback { get; set; }
    }

    /// <summary>
    /// Objective speaking-delivery metrics (pace/pauses/filler words) plus short rule-based comments,
    /// derived purely from Whisper's verbose_json response — no second AI call.
    /// </summary>
    public class DeliveryFeedback
    {
        public required double PaceWpm { get; set; }
        public required int PauseCount { get; set; }
        public required int FillerWordCount { get; set; }
        public required List<string> Comments { get; set; }
    }

    /// <summary>
    /// Request to summarize a finished (or in-progress) conversation
    /// </summary>
    public class ConversationSummaryRequest
    {
        public required ConversationContext ConversationContext { get; set; }
        public required List<ConversationTurn> ConversationHistory { get; set; }
    }

    /// <summary>
    /// End-of-conversation recap: corrections made, more natural phrasings, and
    /// words worth adding to the user's Repository.
    /// </summary>
    public class ConversationSummaryResponse
    {
        public required List<string> CorrectionsReview { get; set; }
        public required List<string> BetterPhrasings { get; set; }
        public required List<string> SuggestedWords { get; set; }
    }
}
