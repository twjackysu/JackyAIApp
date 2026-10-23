using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Mappers;
using JackyAIApp.Server.Services.OpenAI;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>
    /// Generates a word's definition with OpenAI and stores it, once per word: every lookup of a word
    /// being generated waits on the same run, and the run finishes and saves even when nobody waits any
    /// longer (the word page stops waiting after a few seconds, a client disconnects). So a word is paid
    /// for once and read from the database from then on.
    /// </summary>
    public interface IWordGenerator
    {
        /// <summary>
        /// The stored word, generated (or regenerated, if marked invalid) unless another run stored it
        /// first; null when OpenAI gave no usable answer, which is not stored and is tried again by the
        /// next lookup. <paramref name="ct"/> only stops this caller waiting, never the run.
        /// </summary>
        Task<SQL.Word?> GenerateAsync(string lowerWord, CancellationToken ct);
    }

    /// <summary>
    /// A run has its own scope — its own DbContext, OpenAI client and AI usage log — because it outlives
    /// the request that started it. Run in the request's scope, a lookup the page stopped waiting for
    /// was paid for, then failed to save on a disposed context, and the next visit paid again.
    /// </summary>
    public sealed class WordGenerator(
        IServiceScopeFactory scopes, ILogger<WordGenerator> logger, TimeProvider? timeProvider = null) : IWordGenerator
    {
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        // Runs are shared by the callers of this instance (the app has one; each test builds its own).
        private readonly string _flightPrefix = $"word-generation:{Guid.NewGuid():N}:";

        public Task<SQL.Word?> GenerateAsync(string lowerWord, CancellationToken ct) =>
            SingleFlight.RunAsync(_flightPrefix + lowerWord, () => GenerateAndStoreAsync(lowerWord), ct);

        private async Task<SQL.Word?> GenerateAndStoreAsync(string lowerWord)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AzureSQLDBContext>();
            var prompts = scope.ServiceProvider.GetRequiredService<IOpenAIPromptService>();

            // A run that ended a moment ago may have stored it since the caller looked.
            var dbWord = await WordQueries.LoadAsync(db, lowerWord, CancellationToken.None);
            if (dbWord is { DataInvalid: not true })
                return dbWord;

            var (wordbase, error) = await prompts.GetCompletionAsync<WordBase>(
                "Prompt/WordBase/System.txt",
                lowerWord,
                model: NewModels.GPT_6_LUNA,
                examples: new[] { CreateFewShotExample() });

            if (wordbase == null)
            {
                logger.LogError("Failed to generate word definition for {Word}: {Error}", lowerWord, error);
                return null;
            }

            logger.LogInformation("Generated word definition for: {Word}", lowerWord);

            // Stored under the word looked up: the model may answer with another form of it (its lemma),
            // and a row under that form is never found by the next lookup of this one, which would pay again.
            if (!string.Equals(wordbase.Word, lowerWord, StringComparison.OrdinalIgnoreCase))
                wordbase.Word = lowerWord;

            var now = _time.GetUtcNow().UtcDateTime;
            if (dbWord != null)
            {
                UpdateExistingWord(db, dbWord, wordbase, now);
            }
            else
            {
                dbWord = WordMapper.ToEntity(wordbase, now);
                db.Words.Add(dbWord);
            }

            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Word {Word} saved to DB", lowerWord);
            return dbWord;
        }

        private static void UpdateExistingWord(AzureSQLDBContext db, SQL.Word dbWord, WordBase wordbase, DateTime now)
        {
            dbWord.DataInvalid = null;
            dbWord.WordText = wordbase.Word;
            dbWord.KKPhonics = wordbase.KKPhonics;
            dbWord.LastUpdated = now;

            // Clear existing meanings
            if (dbWord.Meanings != null)
            {
                foreach (var meaning in dbWord.Meanings.ToList())
                {
                    db.WordMeanings.Remove(meaning);
                }
                dbWord.Meanings.Clear();
            }
            else
            {
                dbWord.Meanings = new List<SQL.WordMeaning>();
            }

            foreach (var meaningData in wordbase.Meanings)
            {
                dbWord.Meanings.Add(WordMapper.ToMeaningEntity(meaningData, dbWord.Id, dbWord));
            }
        }

        private static (string User, string Assistant) CreateFewShotExample()
        {
            var exampleWord = new WordBase
            {
                Word = "set",
                KKPhonics = "/sɛt/",
                Meanings = new List<WordMeaning>
                {
                    new()
                    {
                        PartOfSpeech = "noun",
                        Definitions = new List<Definition>
                        {
                            new() { English = "A collection of objects that belong together or are used together.", Chinese = "一組屬於或一起使用的物件。" },
                            new() { English = "The way in which something is set, positioned, or arranged.", Chinese = "某物被設置、定位或排列的方式。" }
                        },
                        ExampleSentences = new List<ExampleSentence>
                        {
                            new() { English = "He bought a chess set.", Chinese = "他買了一套西洋棋。" },
                            new() { English = "The set of her skirt is perfect.", Chinese = "她的裙子的設置是完美的。" }
                        },
                        Synonyms = new List<string> { "group", "collection" },
                        Antonyms = new List<string> { "single" },
                        RelatedWords = new List<string> { "kit", "assembly" }
                    },
                    new()
                    {
                        PartOfSpeech = "verb",
                        Definitions = new List<Definition>
                        {
                            new() { English = "To put something in a specified place or position.", Chinese = "將某物放在指定的地方或位置。" },
                            new() { English = "To fix firmly or to make stable.", Chinese = "固定或使穩定。" }
                        },
                        ExampleSentences = new List<ExampleSentence>
                        {
                            new() { English = "She set the book on the table.", Chinese = "她將書放在桌上。" },
                            new() { English = "The concrete will set within a few hours.", Chinese = "混凝土幾小時內就會凝固。" }
                        },
                        Synonyms = new List<string> { "place", "position" },
                        Antonyms = new List<string> { "remove" },
                        RelatedWords = new List<string> { "install", "establish" }
                    }
                }
            };

            return ("set", AiJson.Serialize(exampleWord));
        }
    }
}
