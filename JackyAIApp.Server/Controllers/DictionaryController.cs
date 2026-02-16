using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using WeCantSpell.Hunspell;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DictionaryController : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger;
        private readonly IMyResponseFactory _responseFactory;
        private readonly AzureSQLDBContext _DBContext;
        private readonly IOpenAIPromptService _promptService;
        private readonly IExtendedMemoryCache _memoryCache;

        public DictionaryController(
            ILogger<DictionaryController> logger,
            IMyResponseFactory responseFactory,
            AzureSQLDBContext DBContext,
            IOpenAIPromptService promptService,
            IExtendedMemoryCache memoryCache)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
            _DBContext = DBContext ?? throw new ArgumentNullException(nameof(DBContext));
            _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        [HttpGet("{word}", Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var lowerWord = word.Trim().ToLower();

            // Validate word against dictionary
            var dictionary = WordList.CreateFromFiles("Dictionary/en_US.dic");
            if (!dictionary.Check(lowerWord))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound, "This is not a valid word.");
            }

            // Check cache first
            var cacheKey = $"Get_Dictionary_{lowerWord}";
            Data.Models.SQL.Word? dbWord = null;
            
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedWord) && cachedWord is Data.Models.SQL.Word cachedDbWord)
            {
                dbWord = cachedDbWord;
            }
            else
            {
                dbWord = await _DBContext.Words
                    .Where(x => x.WordText == lowerWord)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Definitions)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.ExampleSentences)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Tags)
                    .SingleOrDefaultAsync();
                    
                _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));
            }
            
            // Return cached/DB result if valid
            if (dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                return _responseFactory.CreateOKResponse(ConvertToDto(dbWord));
            }

            // Generate word definition using OpenAI
            var example = CreateFewShotExample();
            var (wordbase, error) = await _promptService.GetCompletionAsync<WordBase>(
                "Prompt/WordBase/System.txt",
                lowerWord,
                examples: new[] { example });

            if (wordbase == null)
            {
                _logger.LogError("Failed to generate word definition for {Word}: {Error}", lowerWord, error);
                return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, 
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
                dbWord = CreateNewWord(wordbase);
                await _DBContext.Words.AddAsync(dbWord);
            }
            
            await _DBContext.SaveChangesAsync();
            _logger.LogInformation("Word {Word} saved to DB", lowerWord);

            // Update cache
            _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));

            return _responseFactory.CreateOKResponse(ConvertToDto(dbWord));
        }

        [HttpPut("{word}/invalid", Name = "Make a word invalid")]
        public async Task<IActionResult> Invalid(string word)
        {
            var lowerWord = word.Trim().ToLower();
            var cacheKey = $"Get_Dictionary_{lowerWord}";
            _memoryCache.Remove(cacheKey);
            
            var result = await _DBContext.Words
                .Where(x => x.WordText == lowerWord)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Definitions)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.ExampleSentences)
                .Include(w => w.Meanings)
                    .ThenInclude(m => m.Tags)
                .SingleOrDefaultAsync();

            if (result == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound, "Word not found");
            }

            result.DataInvalid = true;
            result.LastUpdated = DateTime.Now;
            await _DBContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse(ConvertToDto(result));
        }

        #region Private Helper Methods

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

        private void UpdateExistingWord(Data.Models.SQL.Word dbWord, WordBase wordbase)
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
                    _DBContext.WordMeanings.Remove(meaning);
                }
                dbWord.Meanings.Clear();
            }
            else
            {
                dbWord.Meanings = new List<Data.Models.SQL.WordMeaning>();
            }
            
            // Create new meanings
            foreach (var meaningData in wordbase.Meanings)
            {
                dbWord.Meanings.Add(CreateMeaningFromDto(meaningData, dbWord.Id, dbWord));
            }
        }

        private static Data.Models.SQL.Word CreateNewWord(WordBase wordbase)
        {
            var newWordId = Guid.NewGuid().ToString();
            var newWord = new Data.Models.SQL.Word
            {
                Id = newWordId,
                WordText = wordbase.Word,
                KKPhonics = wordbase.KKPhonics,
                DateAdded = DateTime.Now,
                LastUpdated = DateTime.Now,
                Meanings = new List<Data.Models.SQL.WordMeaning>()
            };
            
            foreach (var meaningData in wordbase.Meanings)
            {
                newWord.Meanings.Add(CreateMeaningFromDto(meaningData, newWordId, newWord));
            }
            
            return newWord;
        }

        private static Data.Models.SQL.WordMeaning CreateMeaningFromDto(WordMeaning meaningData, string wordId, Data.Models.SQL.Word word)
        {
            var meaningId = Guid.NewGuid().ToString();
            var meaning = new Data.Models.SQL.WordMeaning
            {
                Id = meaningId,
                PartOfSpeech = meaningData.PartOfSpeech,
                WordId = wordId,
                Word = word,
                Definitions = new List<Data.Models.SQL.Definition>(),
                ExampleSentences = new List<Data.Models.SQL.ExampleSentence>(),
                Tags = new List<Data.Models.SQL.WordMeaningTag>()
            };

            // Add definitions
            if (meaningData.Definitions != null)
            {
                foreach (var definitionData in meaningData.Definitions)
                {
                    meaning.Definitions.Add(new Data.Models.SQL.Definition
                    {
                        Id = Guid.NewGuid().ToString(),
                        English = definitionData.English,
                        Chinese = definitionData.Chinese,
                        WordMeaningId = meaningId,
                        WordMeaning = meaning
                    });
                }
            }

            // Add example sentences
            if (meaningData.ExampleSentences != null)
            {
                foreach (var exampleData in meaningData.ExampleSentences)
                {
                    meaning.ExampleSentences.Add(new Data.Models.SQL.ExampleSentence
                    {
                        Id = Guid.NewGuid().ToString(),
                        English = exampleData.English,
                        Chinese = exampleData.Chinese,
                        WordMeaningId = meaningId,
                        WordMeaning = meaning
                    });
                }
            }

            // Add tags
            AddTagsToMeaning(meaning, meaningId, meaningData.Synonyms, Constants.TagTypes.Synonym);
            AddTagsToMeaning(meaning, meaningId, meaningData.Antonyms, Constants.TagTypes.Antonym);
            AddTagsToMeaning(meaning, meaningId, meaningData.RelatedWords, Constants.TagTypes.Related);

            return meaning;
        }

        private static void AddTagsToMeaning(Data.Models.SQL.WordMeaning meaning, string meaningId, List<string>? words, string tagType)
        {
            if (words == null) return;

            foreach (var w in words)
            {
                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                {
                    Id = Guid.NewGuid().ToString(),
                    TagType = tagType,
                    Word = w,
                    WordMeaningId = meaningId,
                    WordMeaning = meaning
                });
            }
        }

        private static DTO.Word ConvertToDto(Data.Models.SQL.Word sqlWord)
        {
            return new DTO.Word
            {
                Id = sqlWord.Id,
                PartitionKey = sqlWord.WordText,
                Word = sqlWord.WordText,
                KKPhonics = sqlWord.KKPhonics,
                DateAdded = sqlWord.DateAdded,
                LastUpdated = sqlWord.LastUpdated,
                DataInvalid = sqlWord.DataInvalid,
                Meanings = sqlWord.Meanings.Select(m => new DTO.WordMeaning
                {
                    PartOfSpeech = m.PartOfSpeech,
                    Definitions = m.Definitions.Select(d => new DTO.Definition
                    {
                        English = d.English,
                        Chinese = d.Chinese
                    }).ToList(),
                    ExampleSentences = m.ExampleSentences.Select(es => new DTO.ExampleSentence
                    {
                        English = es.English,
                        Chinese = es.Chinese
                    }).ToList(),
                    Synonyms = m.Tags.Where(t => t.TagType == Constants.TagTypes.Synonym).Select(t => t.Word).ToList(),
                    Antonyms = m.Tags.Where(t => t.TagType == Constants.TagTypes.Antonym).Select(t => t.Word).ToList(),
                    RelatedWords = m.Tags.Where(t => t.TagType == Constants.TagTypes.Related).Select(t => t.Word).ToList()
                }).ToList()
            };
        }

        #endregion
    }
}
