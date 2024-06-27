using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
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
    public class RepositoryController(ILogger<RepositoryController> logger, IOptionsMonitor<Settings> settings, IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IUserService userService, IMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<RepositoryController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        [HttpGet("word")]
        public async Task<IActionResult> GetWords()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";
            if(!_memoryCache.TryGetValue(cacheKey, out List<Word>? result))
            {
                var list = await _DBContext.PersonalWord.Where(x => x.UserId == userId).Select(x => x.WordId).ToListAsync();
                result = await _DBContext.Word.Where(x => list.Contains(x.Id)).ToListAsync();
                _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
            }
            return responseFactory.CreateOKResponse(result);
        }
        [HttpPut("word/{wordId}")]
        public async Task<IActionResult> AddWord(string wordId)
        {
            if (wordId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest);
            }
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";
            _memoryCache.Remove(cacheKey);
            if (userId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden);
            }
            _logger.LogInformation("user: {userId}, add personal word: {wordId}", userId, wordId);
            var personalWord = new PersonalWord()
            {
                Id = Guid.NewGuid().ToString(),
                PartitionKey = userId,
                CreationDate = DateTime.Now,
                UserId = userId,
                WordId = wordId,
            };

            await _DBContext.PersonalWord.AddAsync(personalWord);
            await _DBContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse(personalWord);
        }
        [HttpDelete("word/{wordId}")]
        public async Task<IActionResult> DeleteWord(string wordId)
        {
            if (wordId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.BadRequest);
            }
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}";
            _memoryCache.Remove(cacheKey);
            if (userId == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.Forbidden);
            }
            _logger.LogInformation("user: {userId}, delete personal word: {wordId}", userId, wordId);

            var personalWord = await _DBContext.PersonalWord
                .FirstOrDefaultAsync(pw => pw.UserId == userId && pw.WordId == wordId);

            if (personalWord == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound);
            }

            _DBContext.PersonalWord.Remove(personalWord);
            await _DBContext.SaveChangesAsync();

            return _responseFactory.CreateOKResponse();
        }
    }
}
