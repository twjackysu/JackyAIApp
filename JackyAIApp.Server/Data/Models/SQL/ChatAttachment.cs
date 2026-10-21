using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A file a user attached to a chat message. A Word, Excel or text file is kept as the text read
    /// out of it (the file itself is not stored); an image or a PDF, which the model reads itself, is
    /// kept as a file in the attachment store, named by <see cref="BlobName"/>. Uploaded before the
    /// message is sent, so <see cref="MessageId"/> is null until then; it is sent to the model with
    /// its message on every later turn of the conversation.
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

        /// <summary>An AttachmentKinds value: "word", "excel", "csv", "text", "image" or "pdf".</summary>
        [Required, MaxLength(16)]
        public required string Kind { get; set; }

        /// <summary>The text read out of the file, at most AttachmentTextExtractor.MaxChars long;
        /// empty for an image or a PDF.</summary>
        [Required]
        public required string Text { get; set; }

        /// <summary>The file had more text than was kept.</summary>
        public bool Truncated { get; set; }

        /// <summary>The stored file of an image or a PDF (IChatAttachmentStore); null for text.</summary>
        [MaxLength(200)]
        public string? BlobName { get; set; }

        /// <summary>The media type of the stored file.</summary>
        [MaxLength(64)]
        public string? MediaType { get; set; }

        /// <summary>Pages of a PDF.</summary>
        public int? PageCount { get; set; }

        /// <summary>The copy of an image or PDF in Anthropic's Files API (IAnthropicFileStore), which
        /// a turn refers to instead of sending the file; null until it has been uploaded there.</summary>
        [MaxLength(100)]
        public string? AnthropicFileId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
