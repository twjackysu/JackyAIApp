using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    public class SentenceTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// The prompt given to the user for sentence formation.
        /// </summary>
        public required string Prompt { get; set; }

        /// <summary>
        /// A sample answer provided by AI for reference.
        /// </summary>
        public required string SampleAnswer { get; set; }

        /// <summary>
        /// Context or scenario for the sentence formation.
        /// </summary>
        public required string Context { get; set; }

        /// <summary>
        /// Difficulty level from 1 (easy) to 5 (hard).
        /// </summary>
        public required int DifficultyLevel { get; set; }

        /// <summary>
        /// Grammar pattern or structure to be used (optional).
        /// </summary>
        public string? GrammarPattern { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
    }
}
