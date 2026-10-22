using System.Linq;
using WeCantSpell.Hunspell;

namespace JackyAIApp.Server.Services.SpellChecker
{
    /// <summary>
    /// Hunspell-backed spell checker. Registered as a singleton because
    /// loading the dictionary files from disk is expensive and the resulting
    /// <see cref="WordList"/> is immutable and thread-safe.
    /// </summary>
    public class HunspellSpellChecker : ISpellChecker
    {
        private const string DictionaryPath = "Dictionary/en_US.dic";

        private readonly WordList _wordList;

        public HunspellSpellChecker()
        {
            _wordList = WordList.CreateFromFiles(DictionaryPath);
        }

        public bool Check(string word) => _wordList.Check(word);

        public IReadOnlyList<string> Suggest(string word) => _wordList.Suggest(word).Take(5).ToList();

        // Hunspell names the list entry an affix rule derived the word from; a word found as an entry
        // itself has none.
        public string? RootOf(string word)
        {
            var result = _wordList.CheckDetails(word);
            return result.Correct && !string.IsNullOrEmpty(result.Root) ? result.Root : null;
        }
    }
}
