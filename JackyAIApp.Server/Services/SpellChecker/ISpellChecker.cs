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
    }
}
