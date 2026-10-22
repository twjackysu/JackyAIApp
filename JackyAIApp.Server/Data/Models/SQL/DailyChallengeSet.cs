using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// The day's daily-challenge questions, generated once (on first demand) and persisted so every
    /// GET and submit that day reads the same set. Regenerating per request is not stable: the
    /// generator draws every shuffle and every option order from one date-seeded random stream, so
    /// a single word added to (or invalidated in) the dictionary between a user's GET and their
    /// submit shifts the whole stream and changes the options and correct index of all questions.
    /// </summary>
    public class DailyChallengeSet
    {
        /// <summary>
        /// The UTC date of the challenge (date only, no time) — same meaning and column type as
        /// <see cref="DailyChallengeResult.ChallengeDate"/>. The key itself, so two requests that
        /// both find the day empty cannot both persist a set.
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public DateTime ChallengeDate { get; set; }

        /// <summary>Serialized <c>List&lt;DailyChallengeQuestion&gt;</c> (System.Text.Json defaults).</summary>
        public required string QuestionsJson { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
