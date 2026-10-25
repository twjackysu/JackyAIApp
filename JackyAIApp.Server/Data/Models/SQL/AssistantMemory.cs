using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Something the reader asked Studio AI to remember about them ("在準備多益，目標 800 分"), kept until they have it
    /// forgotten (docs/ai-native/design/08). Every one goes into Studio AI's prompt as the reader's notes, so each was
    /// put to the reader on a confirmation card before it was kept. Forgetting it deletes the row.
    /// </summary>
    public class AssistantMemory
    {
        [Key, MaxLength(64)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required, MaxLength(255)]
        public required string UserId { get; set; }

        /// <summary>The note in the reader's words, one line (<c>AssistantMemoryService.MaxLength</c>).</summary>
        [Required, MaxLength(200)]
        public required string Text { get; set; }

        /// <summary>When it was kept (UTC).</summary>
        public DateTime CreatedAt { get; set; }
    }
}
