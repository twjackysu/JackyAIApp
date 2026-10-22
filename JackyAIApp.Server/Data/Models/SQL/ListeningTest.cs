using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// TOEIC Part 1 style "look at the picture, pick the best description" listening question.
    /// Standalone (not word-scoped) — pre-seeded via bulk import, not generated per-word.
    /// </summary>
    public class ListeningTest : IStringId
    {
        public required string Id { get; set; }

        /// <summary>
        /// Blob Storage URL of the question's photo.
        /// </summary>
        public required string ImageUrl { get; set; }

        /// <summary>
        /// The image-generation prompt used to create the photo, kept for traceability/regen.
        /// </summary>
        public string? ImagePrompt { get; set; }

        public required DateTime CreatedAt { get; set; }

        public ICollection<ListeningTestOption> Options { get; set; } = new List<ListeningTestOption>();
    }

    public class ListeningTestOption
    {
        public required string Id { get; set; }

        /// <summary>
        /// The spoken statement text, synthesized to audio on demand via TTS.
        /// </summary>
        public required string Text { get; set; }

        public required bool IsCorrect { get; set; }

        /// <summary>
        /// Why this option is (or isn't) the correct description of the photo.
        /// </summary>
        public required string Explanation { get; set; }

        public required string ListeningTestId { get; set; }
        [ForeignKey(nameof(ListeningTestId))]
        public ListeningTest ListeningTest { get; set; } = null!;
    }
}
