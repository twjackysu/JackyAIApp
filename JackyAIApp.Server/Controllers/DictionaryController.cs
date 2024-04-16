
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

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]/{word}")]
    public class DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IOpenAIService openAIService) : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger = logger;
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IOpenAIService _openAIService = openAIService;

        [HttpGet(Name = "Search word")]
        public async Task<IActionResult> Get(string word)
        {
            var lowerWord = word.Trim().ToLower();
            if(lowerWord.Length > 20)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word is too long. The maximum length is 20 characters.");
            }
            var dbWord = _DBContext.Word.SingleOrDefault(x => x.Word == word && (!x.DataInvalid.HasValue || !x.DataInvalid.Value));
            if(dbWord != null)
            {
                return _responseFactory.CreateOKResponse(dbWord);
            }
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a professional Chinese-to-English dictionary system. You can provide the dictionary content for the words submitted by the users, in a fixed format as follows. If the user inputs randomly and the word cannot be found, please return `null`. ref typescript interface: ```interface Word extends WordBase { id: string; partitionKey: string; dateAdded: string; lastUpdated: string; dataInvalid?: boolean; } interface WordBase { word: string; meanings: WordMeaning[]; } interface WordMeaning { partOfSpeech: string; definitions: Definition[]; exampleSentences: ExampleSentence[]; synonyms: string[]; antonyms: string[]; relatedWords: string[]; } interface Definition { english: string; chinese: string; } interface ExampleSentence { english: string; chinese: string; }```"),
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
                Model = Models.Gpt_4_turbo_preview,
            });
            if (completionResult.Successful)
            {

                logger.LogInformation($"Query OpenAI word: {lowerWord}, result: {JsonConvert.SerializeObject(completionResult, Formatting.Indented)}");
                var content = completionResult.Choices.FirstOrDefault()?.Message.Content;
                if (string.IsNullOrEmpty(content))
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIIsNotResponding);
                }
                var wordbase = JsonConvert.DeserializeObject<WordBase>(content);
                if(wordbase == null)
                {
                    return responseFactory.CreateErrorResponse(ErrorCodes.TheWordCannotBeFound);
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
                // await _DBContext.Database.EnsureDeletedAsync();
                await _DBContext.Database.EnsureCreatedAsync();
                await _DBContext.AddAsync(wordDefinition);
                await _DBContext.SaveChangesAsync();
                _logger.LogInformation($"word: {lowerWord} added to DB.");
                return responseFactory.CreateOKResponse(wordDefinition);
            }
            return responseFactory.CreateErrorResponse(ErrorCodes.OpenAIResponseUnsuccessful);
        }
    }
}