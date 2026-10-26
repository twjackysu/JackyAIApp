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

        /// <summary>
        /// The assistant the message is for (a chat profile's key). Finance's by default: the
        /// /finance page sent no profile before there was more than one assistant.
        /// </summary>
        public string Profile { get; set; } = "finance";

        /// <summary>
        /// What the asking page says about itself, for the assistant (Data Analysis: the source on
        /// screen). Short keys and values only; the controller drops anything past its limits.
        /// </summary>
        public Dictionary<string, string>? Context { get; set; }

        /// <summary>
        /// The message quotes text the reader selected on a page (Studio AI's selection bar), which may be
        /// someone else's words: the conversation counts as having read outside text from this turn on
        /// (<see cref="Data.Models.SQL.ChatConversation.ReadOutsideText"/>), so its writes ask first.
        /// </summary>
        public bool QuotesPageText { get; set; }
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

        /// <summary>
        /// The cards an assistant message's tools showed (a JSON array of <c>{ kind, … }</c>, as the
        /// stream's <c>tool_result.card</c>); null for none.
        /// </summary>
        public System.Text.Json.JsonElement? Cards { get; set; }
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

    /// <summary>A conversation's title after it was named (or as it was, when it is not named again).</summary>
    public sealed record ChatTitleResponse(string Title);

    /// <summary>The reader's decisions on the writes an answer is waiting for (one per call; one left out is a no).</summary>
    public sealed class ChatApprovalRequest
    {
        public List<ChatApprovalDecisionDto> Decisions { get; set; } = [];
    }

    public sealed class ChatApprovalDecisionDto
    {
        public string ToolUseId { get; set; } = string.Empty;
        public bool Approve { get; set; }
    }

    /// <summary>The decisions reached the answer waiting for them.</summary>
    public sealed record ChatApprovalResponse(bool Accepted);
}
