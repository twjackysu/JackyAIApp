namespace JackyAIApp.Server.DTO
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
    }

    /// <summary>A conversation summary returned to the client (title from Dify).</summary>
    public class ChatConversationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    /// <summary>One historical exchange (user query + assistant answer).</summary>
    public class ChatMessageDto
    {
        public string Id { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public long CreatedAt { get; set; }
    }
}
