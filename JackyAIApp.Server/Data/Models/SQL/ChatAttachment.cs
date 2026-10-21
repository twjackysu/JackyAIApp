using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A file a user attached to a chat message, kept as the text read out of it (the file itself
    /// is not stored). Uploaded before the message is sent, so <see cref="MessageId"/> is null until
    /// then; it is sent to the model with its message on every later turn of the conversation.
    /// </summary>
    public class ChatAttachment
    {
        [Key]
        public long Id { get; set; }

        /// <summary>Who uploaded it — only they can attach, list or delete it.</summary>
        [Required, MaxLength(128)]
        public required string UserId { get; set; }

        /// <summary>The user message it was sent with; null while it is waiting to be sent.</summary>
        public long? MessageId { get; set; }

        [ForeignKey(nameof(MessageId))]
        public virtual ChatMessage? Message { get; set; }

        [Required, MaxLength(260)]
        public required string FileName { get; set; }

        /// <summary>An AttachmentKinds value: "word", "excel", "csv" or "text".</summary>
        [Required, MaxLength(16)]
        public required string Kind { get; set; }

        /// <summary>The text read out of the file, at most AttachmentTextExtractor.MaxChars long.</summary>
        [Required]
        public required string Text { get; set; }

        /// <summary>The file had more text than was kept.</summary>
        public bool Truncated { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
