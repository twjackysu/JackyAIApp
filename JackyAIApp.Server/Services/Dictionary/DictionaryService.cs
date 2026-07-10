using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Mappers;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.SpellChecker;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Dictionary
{
    public class DictionaryService(
        ILogger<DictionaryService> logger,
        AzureSQLDBContext dbContext,
        IOpenAIPromptService promptService,
        IExtendedMemoryCache memoryCache,
        ISpellChecker spellChecker) : IDictionaryService
    {
        private readonly ILogger<DictionaryService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IOpenAIPromptService _promptService = promptService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly ISpellChecker _spellChecker = spellChecker;

        private static string CacheKey(string lowerWord) => $"Get_Dictionary_{lowerWord}";

        public async Task<ServiceResult> GetWordAsync(string word)
        {
            var lowerWord = word.Trim().ToLower();

            // Validate word against dictionary (shared singleton checker)
            if (!_spellChecker.Check(lowerWord))
            {
                return ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "This is not a valid word.");
            }

            var cacheKey = CacheKey(lowerWord);
            SQL.Word? dbWord;

            if (_memoryCache.TryGetValue(cacheKey, out object? cachedWord) && cachedWord is SQL.Word cachedDbWord)
            {
                dbWord = cachedDbWord;
            }
            else
            {
                dbWord = await LoadWordAsync(lowerWord);
                _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));
            }

            // Return cached/DB result if valid
            if (dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                return ServiceResult.Ok(WordMapper.ToDto(dbWord));
            }

            // Generate word definition using OpenAI
            var example = CreateFewShotExample();
            var (wordbase, error) = await _promptService.GetCompletionAsync<WordBase>(
                "Prompt/WordBase/System.txt",
                lowerWord,
                model: NewModels.GPT_4_1_NANO,
                examples: new[] { example });

            if (wordbase == null)
            {
                _logger.LogError("Failed to generate word definition for {Word}: {Error}", lowerWord, error);
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding word.");
            }

            _logger.LogInformation("Generated word definition for: {Word}", lowerWord);

            // Update or create word in DB
            if (dbWord != null && dbWord.DataInvalid == true)
            {
                UpdateExistingWord(dbWord, wordbase);
            }
            else
            {
                dbWord = WordMapper.ToEntity(wordbase);
                await _dbContext.Words.AddAsync(dbWord);
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Word {Word} saved to DB", lowerWord);

            _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));

            return ServiceResult.Ok(WordMapper.ToDto(dbWord));
        }

        public async Task<ServiceResult> InvalidateWordAsync(string word)
        {
            var lowerWord = word.Trim().ToLower();
            _memoryCache.Remove(CacheKey(lowerWord));

            var result = await LoadWordAsync(lowerWord);
            if (result == null)
            {
                return ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "Word not found");
            }

            result.DataInvalid = true;
            result.LastUpdated = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(WordMapper.ToDto(result));
        }

        public ServiceResult SuggestWords(string word)
        {
            var lowerWord = word.Trim().ToLower();
            if (string.IsNullOrEmpty(lowerWord))
            {
                return ServiceResult.Ok(new WordSuggestionsResponse { Suggestions = new List<string>() });
            }

            var suggestions = _spellChecker.Suggest(lowerWord);
            return ServiceResult.Ok(new WordSuggestionsResponse { Suggestions = suggestions.ToList() });
        }

        private Task<SQL.Word?> LoadWordAsync(string lowerWord) =>
            _dbContext.Words
                .Where(x => x.WordText == lowerWord)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .SingleOrDefaultAsync();

        private void UpdateExistingWord(SQL.Word dbWord, WordBase wordbase)
        {
            dbWord.DataInvalid = null;
            dbWord.WordText = wordbase.Word;
            dbWord.KKPhonics = wordbase.KKPhonics;
            dbWord.LastUpdated = DateTime.Now;

            // Clear existing meanings
            if (dbWord.Meanings != null)
            {
                foreach (var meaning in dbWord.Meanings.ToList())
                {
                    _dbContext.WordMeanings.Remove(meaning);
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

            return ("set", JsonConvert.SerializeObject(exampleWord));
        }
    }
}
