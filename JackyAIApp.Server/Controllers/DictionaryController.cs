using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureSQLDBContext DBContext, IOpenAIService openAIService, IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        [HttpGet("{word}", Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var CleanInput = (string input) =>
            {
                // remove start with ```json or ```, and end with ```
                string pattern = @"^\s*```\s*json\s*|^\s*```\s*|```\s*$";
                string replacement = "";
                string result = Regex.Replace(input, pattern, replacement, RegexOptions.IgnoreCase);
                return result.Trim();
            };

            var lowerWord = word.Trim().ToLower();

            var dictionary = WordList.CreateFromFiles("Dictionary/en_US.dic");
            bool isValid = dictionary.Check(lowerWord);
            if (!isValid)
            {
                return responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound, "This is not a valid word.");
            }
            var cacheKey = $"Get_Dictionary_{lowerWord}";
            if (!_memoryCache.TryGetValue(cacheKey, out Data.Models.SQL.Word? dbWord))

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
            
            if(dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                // Convert SQL entity to DTO to avoid circular reference
                var wordDto = ConvertToDto(dbWord);
                return _responseFactory.CreateOKResponse(wordDto);
            }
            
            string systemChatMessage = System.IO.File.ReadAllText("Prompt/WordBase/System.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("set"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new DTO.WordBase()
                    {
                        Word = "set",
                        KKPhonics = "/sɛt/",
                        Meanings =
                        [
                            new DTO.WordMeaning()
                            {
                                PartOfSpeech = "noun",
                                Definitions =
                                [
                                    new DTO.Definition()
                                    {
                                        English = "A collection of objects that belong together or are used together.",
                                        Chinese = "一組屬於或一起使用的物件。"
                                    },
                                    new DTO.Definition()
                                    {
                                        English = "The way in which something is set, positioned, or arranged.",
                                        Chinese = "某物被設置、定位或排列的方式。"
                                    }
                                ],
                                ExampleSentences =
                                [
                                    new DTO.ExampleSentence()
                                    {
                                        English = "He bought a chess set.",
                                        Chinese = "他買了一套西洋棋。"
                                    },
                                    new DTO.ExampleSentence()
                                    {
                                        English = "The set of her skirt is perfect.",
                                        Chinese = "她的裙子的設置是完美的。"
                                    }
                                ],
                                Synonyms = ["group", "collection"],
                                Antonyms = ["single"],
                                RelatedWords = ["kit", "assembly"]
                            },
                            new DTO.WordMeaning()
                            {
                                PartOfSpeech = "verb",
                                Definitions =
                                [
                                    new DTO.Definition()
                                    {
                                        English = "To put something in a specified place or position.",
                                        Chinese = "將某物放在指定的地方或位置。"
                                    },
                                    new DTO.Definition()
                                    {
                                        English = "To fix firmly or to make stable.",
                                        Chinese = "固定或使穩定。"
                                    }
                                ],
                                ExampleSentences =
                                [
                                    new DTO.ExampleSentence()
                                    {
                                        English = "She set the book on the table.",
                                        Chinese = "她將書放在桌上。"
                                    },
                                    new DTO.ExampleSentence()
                                    {
                                        English = "The concrete will set within a few hours.",
                                        Chinese = "混凝土幾小時內就會凝固。"
                                    }
                                ],
                                Synonyms = ["place", "position"],
                                Antonyms = ["remove"],
                                RelatedWords = ["install", "establish"]
                            }
                        ],
                    })),
                    ChatMessage.FromUser(lowerWord)
                ],
                Model = Models.Gpt_4o_mini,
            });
            var errorMessage = "Query failed, OpenAI could not generate the corresponding word.";
            if (completionResult.Successful)
            {
                _logger.LogInformation("Query OpenAI word: {lowerWord}, result: {json}", lowerWord, JsonConvert.SerializeObject(completionResult, Formatting.Indented));
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                WordBase? wordbase = null;
                try
                {
                    wordbase = JsonConvert.DeserializeObject<WordBase>(CleanInput(content));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the content: {content}", content);
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                if(wordbase == null)
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, errorMessage);
                }
                
                if(dbWord != null && (dbWord.DataInvalid.HasValue && dbWord.DataInvalid.Value))
                {
                    // Update existing word
                    dbWord.DataInvalid = null;
                    dbWord.WordText = wordbase.Word;
                    dbWord.KKPhonics = wordbase.KKPhonics;
                    
                    // Clear existing meanings and recreate
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
                    
                    // Create new meanings from WordBase data
                    foreach (var meaningData in wordbase.Meanings)
                    {
                        var meaningId = Guid.NewGuid().ToString();
                        var meaning = new Data.Models.SQL.WordMeaning
                        {
                            Id = meaningId,
                            PartOfSpeech = meaningData.PartOfSpeech,
                            WordId = dbWord.Id,
                            Word = dbWord
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
                        
                        // Add Synonyms as tags
                        if (meaningData.Synonyms != null)
                        {
                            foreach (var synonym in meaningData.Synonyms)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Synonym",
                                    Word = synonym,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        // Add Antonyms as tags
                        if (meaningData.Antonyms != null)
                        {
                            foreach (var antonym in meaningData.Antonyms)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Antonym",
                                    Word = antonym,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        // Add RelatedWords as tags
                        if (meaningData.RelatedWords != null)
                        {
                            foreach (var relatedWord in meaningData.RelatedWords)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Related",
                                    Word = relatedWord,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        dbWord.Meanings.Add(meaning);
                    }
                    
                    dbWord.LastUpdated = DateTime.Now;
                }
                else
                {
                    // Create new word
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
                    
                    // Create meanings
                    foreach (var meaningData in wordbase.Meanings)
                    {
                        var meaningId = Guid.NewGuid().ToString();
                        var meaning = new Data.Models.SQL.WordMeaning
                        {
                            Id = meaningId,
                            PartOfSpeech = meaningData.PartOfSpeech,
                            WordId = newWordId,
                            Word = newWord,
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
                        
                        // Add Synonyms as tags
                        if (meaningData.Synonyms != null)
                        {
                            foreach (var synonym in meaningData.Synonyms)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Synonym",
                                    Word = synonym,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        // Add Antonyms as tags
                        if (meaningData.Antonyms != null)
                        {
                            foreach (var antonym in meaningData.Antonyms)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Antonym",
                                    Word = antonym,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        // Add RelatedWords as tags
                        if (meaningData.RelatedWords != null)
                        {
                            foreach (var relatedWord in meaningData.RelatedWords)
                            {
                                meaning.Tags.Add(new Data.Models.SQL.WordMeaningTag
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TagType = "Related",
                                    Word = relatedWord,
                                    WordMeaningId = meaningId,
                                    WordMeaning = meaning
                                });
                            }
                        }
                        
                        newWord.Meanings.Add(meaning);
                    }
                    
                    await _DBContext.Words.AddAsync(newWord);
                    dbWord = newWord;
                }
                
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("word: {lowerWord} added to DB.", lowerWord);
                var wordDto = ConvertToDto(dbWord);
                return responseFactory.CreateOKResponse(wordDto);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
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
            if (result != null)
            {
                result.DataInvalid = true;
                result.LastUpdated = DateTime.Now;
                await _DBContext.SaveChangesAsync();
                var wordDto = ConvertToDto(result);
                return responseFactory.CreateOKResponse(wordDto);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound, "Word not found");
        }

        /// <summary>
        /// Converts SQL entity to DTO to avoid circular reference issues
        /// </summary>
        private DTO.Word ConvertToDto(Data.Models.SQL.Word sqlWord)
        {
            return new DTO.Word
            {
                Id = sqlWord.Id,
                PartitionKey = sqlWord.WordText, // Using WordText as partition key
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
                    Synonyms = m.Tags.Where(t => t.TagType == "Synonym").Select(t => t.Word).ToList(),
                    Antonyms = m.Tags.Where(t => t.TagType == "Antonym").Select(t => t.Word).ToList(),
                    RelatedWords = m.Tags.Where(t => t.TagType == "Related").Select(t => t.Word).ToList()
                }).ToList()
            };
        }
    }
}
