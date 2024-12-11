
using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]/{word}")]
    public class DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IOpenAIService openAIService, IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        [HttpGet(Name = "Search word")]
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
            if (!_memoryCache.TryGetValue(lowerWord, out Word? dbWord))
            {
                dbWord = _DBContext.Word.SingleOrDefault(x => x.Word == lowerWord);
                _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));
            }
            if(dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                return _responseFactory.CreateOKResponse(dbWord);
            }
            string systemChatMessage = System.IO.File.ReadAllText("Prompt/WordBase/System.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages =
                [
                    ChatMessage.FromSystem(systemChatMessage),
                    ChatMessage.FromUser("set"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new WordBase()
                    {
                        Word = "set",
                        KKPhonics = "/sɛt/",
                        Meanings =
                        [
                            new WordMeaning()
                            {
                                PartOfSpeech = "noun",
                                Definitions =
                                [
                                    new Definition()
                                    {
                                        English = "A collection of objects that belong together or are used together.",
                                        Chinese = "一組屬於或一起使用的物件。"
                                    },
                                    new Definition()
                                    {
                                        English = "The way in which something is set, positioned, or arranged.",
                                        Chinese = "某物被設置、定位或排列的方式。"
                                    }
                                ],
                                ExampleSentences =
                                [
                                    new ExampleSentence()
                                    {
                                        English = "He bought a chess set.",
                                        Chinese = "他買了一套西洋棋。"
                                    },
                                    new ExampleSentence()
                                    {
                                        English = "The set of her skirt is perfect.",
                                        Chinese = "她的裙子的設置是完美的。"
                                    }
                                ],
                                Synonyms = ["group", "collection"],
                                Antonyms = ["single"],
                                RelatedWords = ["kit", "assembly"]
                            },
                            new WordMeaning()
                            {
                                PartOfSpeech = "verb",
                                Definitions =
                                [
                                    new Definition()
                                    {
                                        English = "To put something in a specified place or position.",
                                        Chinese = "將某物放在指定的地方或位置。"
                                    },
                                    new Definition()
                                    {
                                        English = "To fix firmly or to make stable.",
                                        Chinese = "固定或使穩定。"
                                    }
                                ],
                                ExampleSentences =
                                [
                                    new ExampleSentence()
                                    {
                                        English = "She set the book on the table.",
                                        Chinese = "她將書放在桌上。"
                                    },
                                    new ExampleSentence()
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
                Model = Models.Gpt_4_turbo,
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
                var wordDefinition = new Word()
                {
                    Id = Guid.NewGuid().ToString(),
                    PartitionKey = lowerWord[..2], // the first two letters of a word
                    Word = wordbase.Word,
                    KKPhonics = wordbase.KKPhonics,
                    Meanings = wordbase.Meanings,
                    DateAdded = DateTime.Now,
                    LastUpdated = DateTime.Now,
                };
                if(dbWord != null && (dbWord.DataInvalid.HasValue && dbWord.DataInvalid.Value))
                {
                    dbWord.DataInvalid = null;
                    dbWord.Word = wordDefinition.Word;
                    dbWord.KKPhonics = wordDefinition.KKPhonics;
                    dbWord.Meanings = wordDefinition.Meanings;
                    dbWord.DateAdded = DateTime.Now;
                    dbWord.LastUpdated = DateTime.Now;
                }
                else
                {
                    await _DBContext.AddAsync(wordDefinition);
                }
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("word: {lowerWord} added to DB.", lowerWord);
                return responseFactory.CreateOKResponse(wordDefinition);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }

        [Route("invalid")]
        [HttpPut(Name = "Make a word invalid")]
        public async Task<IActionResult> Invalid(string word)
        {
            var lowerWord = word.Trim().ToLower();
            var cacheKey = $"Get_Dictionary_{lowerWord}";
            _memoryCache.Remove(cacheKey);
            var result = _DBContext.Word.SingleOrDefault(x => x.Word == lowerWord);
            if (result != null)
            {
                result.DataInvalid = true;
                result.LastUpdated = DateTime.Now;
                await _DBContext.SaveChangesAsync();
            }
            return responseFactory.CreateOKResponse(result);
        }
    }
}
