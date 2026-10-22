namespace JackyAIApp.Server.DTO
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }

        /// <summary>Uploaded attachments (POST attachments) to send with this message.</summary>
        public List<long>? AttachmentIds { get; set; }

        /// <summary>
        /// This message is an edit of the reader's earlier message with this id: the conversation goes
        /// back to just before it (it and everything after are removed) and its attachments are sent
        /// again with the new wording.
        /// </summary>
        public long? EditMessageId { get; set; }
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

        /// <summary>Files sent with a user message.</summary>
        public List<ChatAttachmentDto> Attachments { get; set; } = [];
    }

    /// <summary>A file attached to a chat message, as far as the client needs to show it.</summary>
    public class ChatAttachmentDto
    {
        public long Id { get; set; }
        public string FileName { get; set; } = string.Empty;

        /// <summary>"word", "excel", "csv", "text", "image" or "pdf".</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Characters of text read out of the file (after any cut); 0 for an image or a PDF.</summary>
        public int CharCount { get; set; }

        /// <summary>The file had more text than was kept.</summary>
        public bool Truncated { get; set; }

        /// <summary>Pages of a PDF.</summary>
        public int? PageCount { get; set; }
    }

    /// <summary>Whether an answer was still being written when the stop came.</summary>
    public sealed record ChatStopResponse(bool Stopped);
}
