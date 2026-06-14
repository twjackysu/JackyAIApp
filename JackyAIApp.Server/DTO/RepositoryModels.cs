namespace JackyAIApp.Server.DTO
{
    /// <summary>Metadata for a starter word pack (no word list — that's added server-side).</summary>
    public class WordPackDto
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required int WordCount { get; set; }
    }

    /// <summary>Outcome of adding a word pack to the user's repository.</summary>
    public class AddWordPackResultDto
    {
        /// <summary>Words newly added to the repository.</summary>
        public required int Added { get; set; }
        /// <summary>Words already in the repository (skipped).</summary>
        public required int AlreadyHad { get; set; }
        /// <summary>Words that could not be generated/added.</summary>
        public required int Failed { get; set; }
    }
}
