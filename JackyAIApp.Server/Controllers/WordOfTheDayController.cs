using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models;
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
        IMyResponseFactory responseFactory, AzureCosmosDBContext DBContext, IUserService userService, IExtendedMemoryCache memoryCache
        ) : ControllerBase
    {
        private readonly ILogger<WordOfTheDayController> _logger = logger ?? throw new ArgumentNullException();
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException();
        private readonly AzureCosmosDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        [HttpGet(Name = "Get word of the day")]
        public async Task<IActionResult> Get()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWordOfTheDay_{userId}";
            if (!_memoryCache.TryGetValue(cacheKey, out Word? result))
            {
                var words = _DBContext.Word.Where(x => x.DataInvalid == null || x.DataInvalid == false);
                int totalWords = await words.CountAsync();

                Random random = new();
                int randomIndex = random.Next(totalWords);

                result = await words.Skip(randomIndex).FirstOrDefaultAsync();

                if (result != null && (!result.DataInvalid.HasValue || !result.DataInvalid.Value))
                {
                    _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                    return _responseFactory.CreateOKResponse(result);
                }
                else
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.NotFound, "No words found.");
                }
            }
            return _responseFactory.CreateOKResponse(result);
        }
    }
}
