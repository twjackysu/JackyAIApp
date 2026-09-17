using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Records a single exam attempt against a word. Drives the learning loop: each attempt
    /// feeds the word's spaced-repetition schedule and builds per-word performance history.
    /// </summary>
    public class ExamAttempt
    {
        [Key]
        public int Id { get; set; }

        /// <summary>The user who attempted the exam.</summary>
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>The word the exam targeted. Null for word-less exam types (e.g. Listening).</summary>
        public string? WordId { get; set; }

        [ForeignKey(nameof(WordId))]
        public Word? Word { get; set; }

        /// <summary>The listening question attempted, if ExamType is "Listening".</summary>
        public string? ListeningTestId { get; set; }

        [ForeignKey(nameof(ListeningTestId))]
        public ListeningTest? ListeningTest { get; set; }

        /// <summary>The listening question attempted, if ExamType is "ListeningResponse".</summary>
        public string? ListeningResponseTestId { get; set; }

        [ForeignKey(nameof(ListeningResponseTestId))]
        public ListeningResponseTest? ListeningResponseTest { get; set; }

        /// <summary>The reading passage attempted, if ExamType is "Reading".</summary>
        public string? ReadingPassageId { get; set; }

        [ForeignKey(nameof(ReadingPassageId))]
        public ReadingPassage? ReadingPassage { get; set; }

        /// <summary>Exam type: "Cloze" | "Translation" | "Sentence" | "Listening" | "ListeningResponse" | "Reading" | "Writing".</summary>
        public required string ExamType { get; set; }

        /// <summary>Whether the attempt counted as correct.</summary>
        public required bool IsCorrect { get; set; }

        /// <summary>Numeric score 0–100 for graded types (Sentence, Writing); null otherwise.</summary>
        public int? Score { get; set; }

        /// <summary>When the attempt happened (UTC).</summary>
        public required DateTime CreatedAt { get; set; }
    }
}
