using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [ApiController]
    [Route("api/dictionary/[controller]")]
    public class WordOfTheDayController(ILogger<WordOfTheDayController> logger, IOptionsMonitor<Settings> settings, 
        IMyResponseFactory responseFactory, IUserService userService, IExtendedMemoryCache memoryCache, AzureSQLDBContext DBContext
        ) : ControllerBase
    {
        private readonly ILogger<WordOfTheDayController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly AzureSQLDBContext _DBContext = DBContext;

        [HttpGet(Name = "Get word of the day")]
        public async Task<IActionResult> Get()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWordOfTheDay_{userId}";
            if (!_memoryCache.TryGetValue(cacheKey, out Data.Models.SQL.Word? result))
            {
                var words = _DBContext.Words.Where(x => x.DataInvalid == null || x.DataInvalid == false);
                int totalWords = await words.CountAsync();

                Random random = new();
                int randomIndex = random.Next(totalWords);

                result = await words.Skip(randomIndex)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Definitions)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.ExampleSentences)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Tags).FirstOrDefaultAsync();

                if (result != null && (!result.DataInvalid.HasValue || !result.DataInvalid.Value))
                {
                    _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                    // Convert SQL entity to DTO to avoid circular reference
                    var wordDto = ConvertToDto(result);
                    return _responseFactory.CreateOKResponse(wordDto);
                }
                else
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "No words found.");
                }
            }
            // Convert cached result to DTO to avoid circular reference
            if (result != null)
            {
                var cachedWordDto = ConvertToDto(result);
                return _responseFactory.CreateOKResponse(cachedWordDto);
            }
            return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "No words found.");
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
