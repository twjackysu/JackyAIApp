using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// TOEIC Part 2 style "hear a question/statement, pick the best spoken response" listening
    /// question. No image — generated live on demand (like Cloze/Translation/Sentence) and
    /// persisted for reuse once the bank has enough entries, rather than pre-seeded.
    /// </summary>
    public class ListeningResponseTest : IStringId
    {
        public required string Id { get; set; }

        /// <summary>The spoken opening question/statement, synthesized to audio on demand.</summary>
        public required string Prompt { get; set; }

        public required DateTime CreatedAt { get; set; }

        public ICollection<ListeningResponseOption> Options { get; set; } = new List<ListeningResponseOption>();
    }

    public class ListeningResponseOption
    {
        public required string Id { get; set; }

        /// <summary>The spoken response text, synthesized to audio on demand.</summary>
        public required string Text { get; set; }

        public required bool IsCorrect { get; set; }

        /// <summary>Why this response is (or isn't) the best reply to the prompt.</summary>
        public required string Explanation { get; set; }

        public required string ListeningResponseTestId { get; set; }
        [ForeignKey(nameof(ListeningResponseTestId))]
        public ListeningResponseTest ListeningResponseTest { get; set; } = null!;
    }
}
