namespace JackyAIApp.Server.DTO
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
    }

    /// <summary>A conversation summary returned to the client (title from the first user message).</summary>
    public class ChatConversationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One turn (user or assistant) in a conversation.</summary>
    public class ChatMessageDto
    {
        public long Id { get; set; }

        /// <summary>"user" or "assistant".</summary>
        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
