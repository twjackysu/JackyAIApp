namespace JackyAIApp.Server.Services.Repository
{
    /// <summary>A word in the user's collection, as a list names it: the word and its first meaning.</summary>
    public sealed record SavedWord(string Word, string? PartOfSpeech, string? Chinese);

    /// <summary>The words the user added most recently, and how many the collection holds in all.</summary>
    public sealed record SavedWords(int Total, IReadOnlyList<SavedWord> Words)
    {
        /// <summary>The most words one read returns.</summary>
        public const int MaxRecent = 30;
    }
}
