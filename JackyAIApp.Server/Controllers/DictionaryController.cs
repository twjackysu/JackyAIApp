
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
            var dbWord = _DBContext.Word.SingleOrDefault(x => x.Word == word && (!x.DataInvalid.HasValue || !x.DataInvalid.Value));
            if(dbWord != null)
            {
                return _responseFactory.CreateOKResponse(dbWord);
            }
            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
            {
                Messages = new List<ChatMessage>
                {
                    ChatMessage.FromSystem("You are a professional Chinese-to-English dictionary system. You can provide the dictionary content for the words submitted by the users, in a fixed format as follows. If the user inputs randomly and the word cannot be found, please return `null`. ref typescript interface: ```interface WordDefinition {word: string;meanings: WordMeaning[];} interface WordMeaning {partOfSpeech: string;definitions: string[];exampleSentences: ExampleSentence[];synonyms: string[];antonyms: string[];relatedWords: string[];} interface ExampleSentence {english: string;chinese: string;}```"),
                    ChatMessage.FromUser("light"),
                    ChatMessage.FromAssistant("{\"word\":\"light\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[\"the natural agent that stimulates sight and makes things visible\"],\"exampleSentences\":[{\"english\":\"The light was so bright that I had to squint.\",\"chinese\":\"光線太亮，我不得不眯眼。\"}],\"synonyms\":[\"illumination\",\"brightness\"],\"antonyms\":[\"darkness\"],\"relatedWords\":[\"lamp\",\"beam\"]},{\"partOfSpeech\":\"adjective\",\"definitions\":[\"having a considerable or sufficient amount of natural light; not dark\"],\"exampleSentences\":[{\"english\":\"The room is light and airy.\",\"chinese\":\"這個房間光線充足且通風。\"}],\"synonyms\":[\"luminous\",\"bright\"],\"antonyms\":[\"heavy\",\"dark\"],\"relatedWords\":[\"airy\",\"spacious\"]}]}"),
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
