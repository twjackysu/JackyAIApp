using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A floating-chatbot conversation, stored locally instead of on a third-party
    /// platform (previously Dify) — keeps chat history off an external vendor's servers.
    /// </summary>
    public class ChatConversation
    {
        // Explicit length must match ChatMessage.ConversationId's [MaxLength(64)] — SQL Server
        // requires FK columns to be defined with identical length/scale, and EF's default for an
        // unannotated string key is nvarchar(450).
        [Key, MaxLength(64)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required, MaxLength(255)]
        public required string UserId { get; set; }

        /// <summary>
        /// The assistant the conversation belongs to (an <c>IChatProfile</c>'s key: "finance",
        /// "data-analysis"…); it is only ever continued, listed and opened as that one's.
        /// </summary>
        [Required, MaxLength(40)]
        public string Profile { get; set; } = "finance";

        /// <summary>Auto-generated from the first user message; null until the first turn completes.</summary>
        [MaxLength(200)]
        public string? Title { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}
