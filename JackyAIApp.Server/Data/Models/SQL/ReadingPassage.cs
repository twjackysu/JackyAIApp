using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// A short passage-level reading comprehension exercise. Standalone (not word-scoped) and
    /// shared across users — generated live on demand and persisted for reuse, same bank pattern
    /// as <see cref="ListeningResponseTest"/>.
    /// </summary>
    public class ReadingPassage : IStringId
    {
        public required string Id { get; set; }

        public required string Title { get; set; }

        public required string PassageText { get; set; }

        public required DateTime CreatedAt { get; set; }

        public ICollection<ReadingQuestion> Questions { get; set; } = new List<ReadingQuestion>();
    }

    public class ReadingQuestion
    {
        public required string Id { get; set; }

        public required string QuestionText { get; set; }

        public required string PassageId { get; set; }
        [ForeignKey(nameof(PassageId))]
        public ReadingPassage ReadingPassage { get; set; } = null!;

        public ICollection<ReadingQuestionOption> Options { get; set; } = new List<ReadingQuestionOption>();
    }

    public class ReadingQuestionOption
    {
        public required string Id { get; set; }

        public required string Text { get; set; }

        public required bool IsCorrect { get; set; }

        /// <summary>Why this option is (or isn't) correct, in Traditional Chinese.</summary>
        public required string Explanation { get; set; }

        public required string QuestionId { get; set; }
        [ForeignKey(nameof(QuestionId))]
        public ReadingQuestion ReadingQuestion { get; set; } = null!;
    }
}
