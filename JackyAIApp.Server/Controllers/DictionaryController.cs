
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]/{word}")]
    public class DictionaryController(ILogger<DictionaryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext) : ControllerBase
    {
        private readonly ILogger<DictionaryController> _logger = logger;
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory;
        private readonly AzureCosmosDBContext _DBContext = DBContext;

        [HttpGet(Name = "Search word")]
        public async Task<WordDefinition> Get(string word)
        {
            var lowerWord = word.Trim().ToLower();
            // TODO: from open ai
            // TODO: validate
            var wordDefinition = new WordDefinition()
            {
                Id = Guid.NewGuid().ToString(),
                PartitionKey = "",
                Word = lowerWord,
                Meanings = new List<WordMeaning>()
                {
                    new WordMeaning()
                    {
                        PartOfSpeech = "",
                        Definitions = new List<string>(),
                        ExampleSentences = new List<ExampleSentence>(),
                        Synonyms = new List<string>(),
                        Antonyms = new List<string>(),
                        RelatedWords = new List<string>(),
                    },
                },
                DateAdded = DateTime.Now,
                LastUpdated = DateTime.Now,
            };
            // await _DBContext.Database.EnsureDeletedAsync();
            // await _DBContext.Database.EnsureCreatedAsync();
            // await _DBContext.AddAsync(wordDefinition);
            // await _DBContext.SaveChangesAsync();
            _logger.LogInformation($"word: {lowerWord} added");
            return wordDefinition;
        }
    }
}
