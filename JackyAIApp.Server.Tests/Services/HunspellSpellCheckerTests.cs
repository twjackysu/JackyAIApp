using JackyAIApp.Server.Services.SpellChecker;

namespace JackyAIApp.Server.Tests.Services
{
    /// <summary>The real word list (Dictionary/en_US.dic), which decides which entry a lookup shows.</summary>
    public class HunspellSpellCheckerTests
    {
        // Loading the list takes a moment; it is immutable, so the tests share one.
        private static readonly Lazy<HunspellSpellChecker> Checker = new(() => new HunspellSpellChecker());

        [Theory]
        [InlineData("establishing", "establish")]
        [InlineData("established", "establish")]
        [InlineData("establishes", "establish")]
        [InlineData("apples", "apple")]
        [InlineData("studies", "study")]
        public void RootOf_ARegularInflection_IsItsListEntry(string word, string root) =>
            Assert.Equal(root, Checker.Value.RootOf(word));

        [Theory]
        [InlineData("establish")] // a base word
        [InlineData("went")]      // an irregular form, an entry of its own
        [InlineData("studied")]   // a form the list holds on its own
        [InlineData("rejector")]  // not a word
        public void RootOf_AnythingElse_IsNull(string word) =>
            Assert.Null(Checker.Value.RootOf(word));
    }
}
