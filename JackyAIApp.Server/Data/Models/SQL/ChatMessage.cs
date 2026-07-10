using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>One turn (user or assistant) in a <see cref="ChatConversation"/>.</summary>
    public class ChatMessage
    {
        [Key]
        public long Id { get; set; }

        [Required, MaxLength(64)]
        public required string ConversationId { get; set; }

        [ForeignKey(nameof(ConversationId))]
        public virtual ChatConversation? Conversation { get; set; }

        /// <summary>"user" or "assistant".</summary>
        [Required, MaxLength(20)]
        public required string Role { get; set; }

        [Required]
        public required string Content { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
