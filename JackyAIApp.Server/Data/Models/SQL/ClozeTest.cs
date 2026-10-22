using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    public class ClozeTest
    {
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets the question text for the cloze test.
        /// </summary>
        public required string Question { get; set; }

        /// <summary>
        /// Gets or sets the correct answer for the cloze test.
        /// </summary>
        public required string Answer { get; set; }

        public required string WordId { get; set; }
        [ForeignKey(nameof(WordId))]
        public Word Word { get; set; } = null!;
        public ICollection<ClozeTestOption> Options { get; set; } = new List<ClozeTestOption>();
    }

    public class ClozeTestOption
    {
        public required string Id { get; set; }

        /// <summary>
        /// The text of the option.
        /// </summary>
        public required string OptionText { get; set; }

        public required string ClozeTestId { get; set; }
        [ForeignKey(nameof(ClozeTestId))]
        public ClozeTest ClozeTest { get; set; } = null!;
    }
}
