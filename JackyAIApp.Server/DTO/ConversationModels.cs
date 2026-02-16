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
    }
}
