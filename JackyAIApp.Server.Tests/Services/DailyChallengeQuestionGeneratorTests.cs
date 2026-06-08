using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.DailyChallenge;

namespace JackyAIApp.Server.Tests.Services
{
    public class DailyChallengeQuestionGeneratorTests
    {
        private readonly DailyChallengeQuestionGenerator _generator = new();

        [Fact]
        public void Generate_ReturnsConfiguredNumberOfQuestions()
        {
            var words = BuildWords(30);

            var questions = _generator.Generate(words, new DateTime(2026, 6, 8));

            Assert.Equal(DailyChallengeConstants.QuestionsPerChallenge, questions.Count);
        }

        [Fact]
        public void Generate_IsDeterministicForSameDate()
        {
            var words = BuildWords(30);
            var date = new DateTime(2026, 6, 8);

            var first = _generator.Generate(words, date);
            var second = _generator.Generate(words, date);

            Assert.Equal(
                first.Select(q => (q.Prompt, q.CorrectIndex, string.Join("|", q.Options))),
                second.Select(q => (q.Prompt, q.CorrectIndex, string.Join("|", q.Options))));
        }

        [Fact]
        public void Generate_EachQuestionHasCorrectIndexWithinOptions()
        {
            var words = BuildWords(30);

            var questions = _generator.Generate(words, new DateTime(2026, 6, 8));

            Assert.All(questions, q =>
            {
                Assert.InRange(q.CorrectIndex, 0, q.Options.Count - 1);
                Assert.Equal(DailyChallengeConstants.OptionsPerQuestion, q.Options.Count);
            });
        }

        private static List<Word> BuildWords(int count)
        {
            var words = new List<Word>();
            for (int i = 0; i < count; i++)
            {
                var wordId = Guid.NewGuid().ToString();
                var meaningId = Guid.NewGuid().ToString();
                var meaning = new WordMeaning
                {
                    Id = meaningId,
                    PartOfSpeech = "noun",
                    WordId = wordId,
                    Definitions = new List<Definition>
                    {
                        new() { Id = Guid.NewGuid().ToString(), English = $"Definition {i}", Chinese = $"定義 {i}", WordMeaningId = meaningId }
                    },
                    ExampleSentences = new List<ExampleSentence>
                    {
                        new() { Id = Guid.NewGuid().ToString(), English = $"This is word{i} in a sentence.", Chinese = $"範例 {i}", WordMeaningId = meaningId }
                    },
                    Tags = new List<WordMeaningTag>
                    {
                        new() { Id = Guid.NewGuid().ToString(), TagType = "Synonym", Word = $"syn{i}", WordMeaningId = meaningId }
                    }
                };
                words.Add(new Word
                {
                    Id = wordId,
                    WordText = $"word{i}",
                    KKPhonics = "/x/",
                    DateAdded = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    Meanings = new List<WordMeaning> { meaning }
                });
            }
            return words;
        }
    }
}
