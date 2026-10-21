using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.WordOfTheDay
{
    public class WordOfTheDayService(
        IUserService userService,
        IExtendedMemoryCache memoryCache,
        AzureSQLDBContext dbContext) : IWordOfTheDayService
    {
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly AzureSQLDBContext _dbContext = dbContext;

        public async Task<ServiceResult<DTO.Word>> GetWordOfTheDayAsync()
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWordOfTheDay_{userId}";
            if (!_memoryCache.TryGetValue(cacheKey, out SQL.Word? result))
            {
                var words = _dbContext.Words.Where(x => x.DataInvalid == null || x.DataInvalid == false);
                int totalWords = await words.CountAsync();

                int randomIndex = Random.Shared.Next(totalWords);

                result = await words.Skip(randomIndex)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Definitions)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.ExampleSentences)
                    .Include(w => w.Meanings)
                        .ThenInclude(m => m.Tags)
                    .FirstOrDefaultAsync();

                if (result != null && (!result.DataInvalid.HasValue || !result.DataInvalid.Value))
                {
                    _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                    return ServiceResult.Ok(WordMapper.ToDto(result));
                }

                return ServiceResult.Fail(ErrorCodes.NotFound, "No words found.");
            }

            if (result != null)
            {
                return ServiceResult.Ok(WordMapper.ToDto(result));
            }

            return ServiceResult.Fail(ErrorCodes.NotFound, "No words found.");
        }
    }
}
