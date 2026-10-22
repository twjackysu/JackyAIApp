namespace JackyAIApp.Server.Services.SpellChecker
{
    /// <summary>
    /// Abstraction over a spell-checking dictionary so the underlying
    /// (expensive to load) word list can be shared as a singleton.
    /// </summary>
    public interface ISpellChecker
    {
        /// <summary>Returns true when <paramref name="word"/> is a valid dictionary word.</summary>
        bool Check(string word);

        /// <summary>Returns spelling suggestions for <paramref name="word"/>, closest match first.</summary>
        IReadOnlyList<string> Suggest(string word);

        /// <summary>
        /// The word <paramref name="word"/> is a regular inflection of — "establishing", "established" and
        /// "establishes" give "establish", "apples" gives "apple" — or null when it is in the word list
        /// itself (a base word, an irregular form such as "went", a form listed on its own such as
        /// "studied") or is not a word at all.
        /// </summary>
        string? RootOf(string word);
    }
}
