
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OpenAI.Interfaces;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]/{word}")]
    public class DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IOpenAIService openAIService) : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IOpenAIService _openAIService = openAIService;

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
            await _DBContext.Database.EnsureCreatedAsync();
            var lowerWord = word.Trim().ToLower();

            var dictionary = WordList.CreateFromFiles("Dictionary/en_US.dic");
            bool isValid = dictionary.Check(lowerWord);
            if (!isValid)
            {
                return responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound, "This is not a valid word.");
            }
            var dbWord = _DBContext.Word.SingleOrDefault(x => x.Word == word);
            if(dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                return _responseFactory.CreateOKResponse(dbWord);
            }
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a professional Chinese-to-English dictionary system designed to provide complete dictionary entries for the words submitted by the users. Always return as much information as possible for each word, including part of speech, definitions, example sentences, synonyms, antonyms, and related words. Here's the expected behavior:\n- If the word can be found, return the full dictionary entry with all available details.\n- If only partial information is available, provide whatever details are available, but at least include 'partOfSpeech' and 'exampleSentences'.\n- If the word cannot be found, check for spelling mistakes, suggest similar words, and return these suggestions. If no suitable suggestions can be found, return `null`.\n\nPlease note that the system should not selectively provide information but should aim to give a comprehensive view of each word based on the available data. Follow this format:\nreference typescript interface: ```interface Word extends WordBase { id: string; partitionKey: string; dateAdded: string; lastUpdated: string; dataInvalid?: boolean; } interface WordBase { word: string; meanings: WordMeaning[]; } interface WordMeaning { partOfSpeech: string; definitions: Definition[]; exampleSentences: ExampleSentence[]; synonyms: string[]; antonyms: string[]; relatedWords: string[]; } interface Definition { english: string; chinese: string; } interface ExampleSentence { english: string; chinese: string; }```"),
                    ChatMessage.FromUser("set"),
                    ChatMessage.FromAssistant(JsonConvert.SerializeObject(new WordBase()
                    {
                        Word = "set",
                        Meanings = new List<WordMeaning>()
                        {
                            new WordMeaning()
                            {
                                PartOfSpeech = "noun",
                                Definitions = new List<Definition>()
                                {
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
                                },
                                ExampleSentences = new List<ExampleSentence>()
                                {
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
                                },
                                Synonyms = new List<string> { "group", "collection" },
                                Antonyms = new List<string> { "single" },
                                RelatedWords = new List<string> { "kit", "assembly" }
                            },
                            new WordMeaning()
                            {
                                PartOfSpeech = "verb",
                                Definitions = new List<Definition>()
                                {
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
                                },
                                ExampleSentences = new List<ExampleSentence>()
                                {
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
                                },
                                Synonyms = new List<string> { "place", "position" },
                                Antonyms = new List<string> { "remove" },
                                RelatedWords = new List<string> { "install", "establish" }
                            }
                        },
                    })),
                    ChatMessage.FromUser(lowerWord)
                },
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
                    Meanings = wordbase.Meanings,
                    DateAdded = DateTime.Now,
                    LastUpdated = DateTime.Now,
                };
                await _DBContext.AddAsync(wordDefinition);
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation("word: {lowerWord} added to DB.", lowerWord);
                return responseFactory.CreateOKResponse(wordDefinition);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful, errorMessage);
        }
    }
}
