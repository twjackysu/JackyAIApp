using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    public class TranslationTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// Sentence to be translated (Traditional Chinese)
        /// </summary>
        public required string Chinese { get; set; }

        /// <summary>
        /// User's English translation
        /// </summary>
        public required string English { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
    }
}
