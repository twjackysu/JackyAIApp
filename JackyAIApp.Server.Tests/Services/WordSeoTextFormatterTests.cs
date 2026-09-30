using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Dictionary;

namespace JackyAIApp.Server.Tests.Services
{
    public class WordSeoTextFormatterTests
    {
        [Fact]
        public void FormatTitle_ReturnsBrandPrefixWithWord()
        {
            var title = WordSeoTextFormatter.FormatTitle("apple");

            Assert.Equal("English Coach - apple", title);
        }

        [Fact]
        public void FormatDescription_UsesFirstMeaningWithDefinitions()
        {
            var meanings = new List<WordMeaning>
            {
                new()
                {
                    PartOfSpeech = "noun",
                    Definitions = new List<Definition>
                    {
                        new() { English = "A round fruit with red or green skin.", Chinese = "蘋果" }
                    },
                    ExampleSentences = new List<ExampleSentence>(),
                    Synonyms = new List<string>(),
                    Antonyms = new List<string>(),
                    RelatedWords = new List<string>()
                }
            };

            var description = WordSeoTextFormatter.FormatDescription("apple", meanings);

            Assert.StartsWith("apple (noun):", description);
            Assert.Contains("A round fruit with red or green skin.", description);
        }

        [Fact]
        public void FormatDescription_SkipsMeaningsWithNoDefinitions()
        {
            var meanings = new List<WordMeaning>
            {
                new()
                {
                    PartOfSpeech = "verb",
                    Definitions = new List<Definition>(),
                    ExampleSentences = new List<ExampleSentence>(),
                    Synonyms = new List<string>(),
                    Antonyms = new List<string>(),
                    RelatedWords = new List<string>()
                },
                new()
                {
                    PartOfSpeech = "noun",
                    Definitions = new List<Definition>
                    {
                        new() { English = "Something to eat.", Chinese = "食物" }
                    },
                    ExampleSentences = new List<ExampleSentence>(),
                    Synonyms = new List<string>(),
                    Antonyms = new List<string>(),
                    RelatedWords = new List<string>()
                }
            };

            var description = WordSeoTextFormatter.FormatDescription("food", meanings);

            Assert.StartsWith("food (noun):", description);
        }

        [Fact]
        public void FormatDescription_NoDefinitions_ReturnsEmpty()
        {
            var meanings = new List<WordMeaning>();

            var description = WordSeoTextFormatter.FormatDescription("xyz", meanings);

            Assert.Equal(string.Empty, description);
        }

        [Fact]
        public void FormatDescription_LongDefinition_TruncatesWithEllipsis()
        {
            var longText = new string('a', 300);
            var meanings = new List<WordMeaning>
            {
                new()
                {
                    PartOfSpeech = "noun",
                    Definitions = new List<Definition> { new() { English = longText, Chinese = "x" } },
                    ExampleSentences = new List<ExampleSentence>(),
                    Synonyms = new List<string>(),
                    Antonyms = new List<string>(),
                    RelatedWords = new List<string>()
                }
            };

            var description = WordSeoTextFormatter.FormatDescription("word", meanings);

            Assert.True(description.Length <= 160);
            Assert.EndsWith("…", description);
        }
    }
}
