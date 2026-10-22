using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Records a user's daily challenge completion result.
    /// </summary>
    public class DailyChallengeResult
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The user who completed the challenge.
        /// </summary>
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>
        /// The date of the challenge (date only, no time).
        /// </summary>
        public required DateTime ChallengeDate { get; set; }

        /// <summary>
        /// Number of correct answers.
        /// </summary>
        public required int Score { get; set; }

        /// <summary>
        /// Total number of questions in the challenge.
        /// </summary>
        public required int TotalQuestions { get; set; }

        /// <summary>
        /// XP earned from this challenge.
        /// </summary>
        public required int XPEarned { get; set; }

        /// <summary>
        /// When the challenge was completed.
        /// </summary>
        public required DateTime CompletedAt { get; set; }
    }
}
