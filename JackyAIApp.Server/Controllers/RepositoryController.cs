using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JackyAIApp.Server.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RepositoryController(ILogger<RepositoryController> logger, IOptionsMonitor<Settings> settings, 
        IMyResponseFactory responseFactory, AzureSQLDBContext DBContext, IUserService userService, IExtendedMemoryCache memoryCache
        ) : ControllerBase
    {
        private readonly ILogger<RepositoryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        [HttpGet("word")]
        public async Task<IActionResult> GetWords(int pageNumber = 1, int pageSize = 10)
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}_Page_{pageNumber}_Size_{pageSize}";
            if (!_memoryCache.TryGetValue(cacheKey, out List<DTO.Word>? result))
            {
                var user = await _DBContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
                if (user == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
                }
                
                // Get words through UserWords relationship
                var sqlWords = await _DBContext.UserWords
                    .Where(uw => uw.UserId == userId)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.Meanings)
                            .ThenInclude(m => m.Definitions)
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.Meanings)
                            .ThenInclude(m => m.ExampleSentences)
                    .Include(uw => uw.Word)
                        .ThenInclude(w => w.Meanings)
                            .ThenInclude(m => m.Tags)
                    .Select(uw => uw.Word)
                    .ToListAsync();
                
                // Convert to DTOs to avoid circular reference
                result = sqlWords.Select(ConvertToDto).ToList();
                    
                _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
            }
            return _responseFactory.CreateOKResponse(result);
        }
        
        [HttpGet("word/{wordId}")]
        public async Task<IActionResult> GetWords(string wordId)
        {
            var userId = _userService.GetUserId();

            var cacheKey = $"GetWords_{userId}_WordId_{wordId}";
            if (!_memoryCache.TryGetValue(cacheKey, out DTO.Word? result))
            {
                var userWord = await _DBContext.UserWords
                    .Where(uw => uw.UserId == userId && uw.WordId == wordId)
                    .SingleOrDefaultAsync();
                
                if (userWord != null)
                {
                    var sqlWord = await _DBContext.Words
                        .Include(w => w.Meanings)
                            .ThenInclude(m => m.Definitions)
                        .Include(w => w.Meanings)
                            .ThenInclude(m => m.ExampleSentences)
                        .Include(w => w.Meanings)
                            .ThenInclude(m => m.Tags)
                        .Include(w => w.ClozeTests)
                            .ThenInclude(ct => ct.Options)
                        .Include(w => w.TranslationTests)
                        .SingleOrDefaultAsync(w => w.Id == wordId);
                    
                    if (sqlWord != null)
                    {
                        // Convert to DTO to avoid circular reference
                        result = ConvertToDto(sqlWord);
                    }
                    
                    _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                }
            }
            return _responseFactory.CreateOKResponse(result);
        }

        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();

            var user = await _DBContext.Users
                .Include(u => u.UserWords)
                .SingleOrDefaultAsync(x => x.Id == userId);
                
            if (user == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden, "User not found.");
            }

            var existingUserWord = await _DBContext.UserWords
                .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == wordId);
                
            if (existingUserWord != null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word has already been added.");
            }

            _logger.LogInformation("User: {userId}, Adding personal word: {wordId}", userId, wordId);

            var userWord = new UserWord
            {
                UserId = userId!,
                WordId = wordId,
                DateAdded = DateTime.UtcNow
            };
            
            await _DBContext.UserWords.AddAsync(userWord);
            
            user.LastUpdated = DateTime.UtcNow;
            
            await _DBContext.SaveChangesAsync();

            var cacheKeyPrefix = $"GetWords_{userId}";
            _memoryCache.ClearCacheByContains(cacheKeyPrefix);

            return _responseFactory.CreateOKResponse(user);
        }

        [HttpDelete("word/{wordId}")]
        public async Task<IActionResult> DeleteWord(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();

            var userWord = await _DBContext.UserWords
                .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == wordId);
                
            if (userWord == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "The word ID was not found in your collection.");
            }

            _logger.LogInformation("User: {userId}, Deleting personal word: {wordId}", userId, wordId);

            _DBContext.UserWords.Remove(userWord);
            
            // Update the user's LastUpdated timestamp
            var user = await _DBContext.Users.FindAsync(userId);
            if (user != null)
            {
                user.LastUpdated = DateTime.UtcNow;
            }

            await _DBContext.SaveChangesAsync();

            var cacheKeyPrefix = $"GetWords_{userId}";
            _memoryCache.ClearCacheByContains(cacheKeyPrefix);

            return _responseFactory.CreateOKResponse();
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
                ClozeTests = sqlWord.ClozeTests?.Select(ct => new DTO.ClozeTest
                {
                    Question = ct.Question,
                    Answer = ct.Answer,
                    Options = ct.Options.Select(o => o.OptionText).ToList()
                }).ToList(),
                TranslationTests = sqlWord.TranslationTests?.Select(tt => new DTO.TranslationTest
                {
                    Chinese = tt.Chinese,
                    English = tt.English
                }).ToList(),
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
