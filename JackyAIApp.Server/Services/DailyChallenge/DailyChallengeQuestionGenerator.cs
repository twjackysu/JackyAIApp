using JackyAIApp.Server.DTO;
using SQL = JackyAIApp.Server.Data.Models.SQL;
using static JackyAIApp.Server.Services.DailyChallenge.DailyChallengeConstants;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    public class DailyChallengeQuestionGenerator : IDailyChallengeQuestionGenerator
    {
        /// <summary>
        /// Generate deterministic questions for a given date using the date as a seed.
        /// </summary>
        public List<DailyChallengeQuestion> Generate(List<SQL.Word> allWords, DateTime date)
        {
            int seed = date.Year * 10000 + date.Month * 100 + date.Day;
            var rng = new Random(seed);

            var shuffled = allWords.OrderBy(_ => rng.Next()).ToList();
            return Build(shuffled.Take(QuestionsPerChallenge).ToList(), shuffled, rng);
        }

        public List<DailyChallengeQuestion> GenerateFor(IReadOnlyList<SQL.Word> targets, IReadOnlyList<SQL.Word> pool, int seed) =>
            Build(targets, pool, new Random(seed));

        /// <summary>
        /// One question per target, in order, its type taking turns; the wrong options come from <paramref name="pool"/>
        /// (the target itself left out).
        /// </summary>
        private static List<DailyChallengeQuestion> Build(IReadOnlyList<SQL.Word> targets, IReadOnlyList<SQL.Word> pool, Random rng)
        {
            var questions = new List<DailyChallengeQuestion>();
            var questionTypes = new[]
            {
                QuestionType.VocabularyDefinition,
                QuestionType.FillInTheBlank,
                QuestionType.Translation,
                QuestionType.Synonym,
                QuestionType.VocabularyDefinition
            };

            for (int i = 0; i < targets.Count; i++)
            {
                var targetWord = targets[i];
                var otherWords = pool.Where(w => w.Id != targetWord.Id).ToList();
                var type = questionTypes[i % questionTypes.Length];

                var question = type switch
                {
                    QuestionType.FillInTheBlank => TryGenerateFillInBlank(i, targetWord, otherWords, rng),
                    QuestionType.Translation => TryGenerateTranslation(i, targetWord, otherWords, rng),
                    QuestionType.Synonym => TryGenerateSynonymOrAntonym(i, targetWord, otherWords, rng, isSynonym: true),
                    QuestionType.Antonym => TryGenerateSynonymOrAntonym(i, targetWord, otherWords, rng, isSynonym: false),
                    _ => null
                };

                question ??= GenerateVocabularyDefinition(i, targetWord, otherWords, rng);
                questions.Add(question);
            }

            return questions;
        }

        private static DailyChallengeQuestion GenerateVocabularyDefinition(int id, SQL.Word word, List<SQL.Word> others, Random rng)
        {
            var meaning = word.Meanings.FirstOrDefault(m => m.Definitions.Any());
            var definition = meaning?.Definitions.First();
            string correctAnswer = definition?.English ?? "No definition available";
            string explanation = definition?.Chinese ?? "";

            var wrongDefinitions = others
                .SelectMany(w => w.Meanings)
                .SelectMany(m => m.Definitions)
                .Select(d => d.English)
                .Where(d => d != correctAnswer)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OptionsPerQuestion - 1)
                .ToList();

            while (wrongDefinitions.Count < OptionsPerQuestion - 1)
                wrongDefinitions.Add($"An unrelated definition #{wrongDefinitions.Count + 1}");

            var options = wrongDefinitions.Append(correctAnswer).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(correctAnswer);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.VocabularyDefinition,
                Prompt = $"What is the meaning of \"{word.WordText}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{word.WordText}: {correctAnswer}" + (string.IsNullOrEmpty(explanation) ? "" : $" ({explanation})"),
                WordId = word.Id,
                Word = word.WordText
            };
        }

        private static DailyChallengeQuestion? TryGenerateFillInBlank(int id, SQL.Word word, List<SQL.Word> others, Random rng)
        {
            var example = word.Meanings
                .SelectMany(m => m.ExampleSentences)
                .FirstOrDefault(e => e.English.Contains(word.WordText, StringComparison.OrdinalIgnoreCase));

            if (example == null) return null;

            string blankedSentence = example.English.Replace(word.WordText, "_____", StringComparison.OrdinalIgnoreCase);
            if (blankedSentence == example.English) return null;

            var wrongWords = others
                .Select(w => w.WordText)
                .Where(w => w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OptionsPerQuestion - 1)
                .ToList();

            while (wrongWords.Count < OptionsPerQuestion - 1)
                wrongWords.Add($"word{wrongWords.Count + 1}");

            var options = wrongWords.Append(word.WordText).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(word.WordText);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.FillInTheBlank,
                Prompt = $"Fill in the blank: {blankedSentence}",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"Answer: {word.WordText}. Full sentence: {example.English}" +
                    (string.IsNullOrEmpty(example.Chinese) ? "" : $" ({example.Chinese})"),
                WordId = word.Id,
                Word = word.WordText
            };
        }

        private static DailyChallengeQuestion? TryGenerateTranslation(int id, SQL.Word word, List<SQL.Word> others, Random rng)
        {
            var definition = word.Meanings
                .SelectMany(m => m.Definitions)
                .FirstOrDefault(d => !string.IsNullOrEmpty(d.Chinese));

            if (definition == null) return null;

            var wrongWords = others
                .Select(w => w.WordText)
                .Where(w => w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OptionsPerQuestion - 1)
                .ToList();

            while (wrongWords.Count < OptionsPerQuestion - 1)
                wrongWords.Add($"word{wrongWords.Count + 1}");

            var options = wrongWords.Append(word.WordText).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(word.WordText);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = QuestionType.Translation,
                Prompt = $"Which English word means \"{definition.Chinese}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{definition.Chinese} = {word.WordText}: {definition.English}",
                WordId = word.Id,
                Word = word.WordText
            };
        }

        private static DailyChallengeQuestion? TryGenerateSynonymOrAntonym(int id, SQL.Word word, List<SQL.Word> others, Random rng, bool isSynonym)
        {
            var tags = word.Meanings
                .SelectMany(m => m.Tags)
                .Where(t => t.TagType == (isSynonym ? "Synonym" : "Antonym"))
                .Select(t => t.Word)
                .Distinct()
                .ToList();

            if (!tags.Any()) return null;

            string correctAnswer = tags.First();
            string label = isSynonym ? "synonym" : "antonym";

            var wrongOptions = others
                .Select(w => w.WordText)
                .Where(w => w != correctAnswer && w != word.WordText)
                .Distinct()
                .OrderBy(_ => rng.Next())
                .Take(OptionsPerQuestion - 1)
                .ToList();

            while (wrongOptions.Count < OptionsPerQuestion - 1)
                wrongOptions.Add($"word{wrongOptions.Count + 1}");

            var options = wrongOptions.Append(correctAnswer).OrderBy(_ => rng.Next()).ToList();
            int correctIndex = options.IndexOf(correctAnswer);

            return new DailyChallengeQuestion
            {
                Id = id,
                Type = isSynonym ? QuestionType.Synonym : QuestionType.Antonym,
                Prompt = $"Which word is a {label} of \"{word.WordText}\"?",
                Options = options,
                CorrectIndex = correctIndex,
                Explanation = $"{correctAnswer} is a {label} of {word.WordText}",
                WordId = word.Id,
                Word = word.WordText
            };
        }
    }
}
