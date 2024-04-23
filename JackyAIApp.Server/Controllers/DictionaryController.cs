
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
            string systemChatMessage = System.IO.File.ReadAllText("Prompt/WordBase/System.txt");
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem(systemChatMessage),
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
                                        Chinese = "�@���ݩ�Τ@�_�ϥΪ�����C"
                                    },
                                    new Definition()
                                    {
                                        English = "The way in which something is set, positioned, or arranged.",
                                        Chinese = "�Y���Q�]�m�B�w��αƦC���覡�C"
                                    }
                                },
                                ExampleSentences = new List<ExampleSentence>()
                                {
                                    new ExampleSentence()
                                    {
                                        English = "He bought a chess set.",
                                        Chinese = "�L�R�F�@�M��v�ѡC"
                                    },
                                    new ExampleSentence()
                                    {
                                        English = "The set of her skirt is perfect.",
                                        Chinese = "�o���Ȥl���]�m�O�������C"
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
                                        Chinese = "�N�Y����b���w���a��Φ�m�C"
                                    },
                                    new Definition()
                                    {
                                        English = "To fix firmly or to make stable.",
                                        Chinese = "�T�w�Ψ�í�w�C"
                                    }
                                },
                                ExampleSentences = new List<ExampleSentence>()
                                {
                                    new ExampleSentence()
                                    {
                                        English = "She set the book on the table.",
                                        Chinese = "�o�N�ѩ�b��W�C"
                                    },
                                    new ExampleSentence()
                                    {
                                        English = "The concrete will set within a few hours.",
                                        Chinese = "�V���g�X�p�ɤ��N�|���T�C"
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
