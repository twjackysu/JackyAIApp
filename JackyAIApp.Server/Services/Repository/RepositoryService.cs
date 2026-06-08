using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Repository
{
    public class RepositoryService(
        ILogger<RepositoryService> logger,
        AzureSQLDBContext dbContext,
        IUserService userService,
        IExtendedMemoryCache memoryCache) : IRepositoryService
    {
        private readonly ILogger<RepositoryService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        public async Task<ServiceResult> GetWordsAsync(int pageNumber, int pageSize)
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}_Page_{pageNumber}_Size_{pageSize}";
            if (!_memoryCache.TryGetValue(cacheKey, out List<DTO.Word>? result))
            {
                var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
                if (user == null)
                {
                    return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
                }

                var sqlWords = await _dbContext.UserWords
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

                result = sqlWords.Select(WordMapper.ToDto).ToList();
                _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
            }
            return ServiceResult.Ok(result);
        }

        public async Task<ServiceResult> GetWordAsync(string wordId)
        {
            var userId = _userService.GetUserId();
            var cacheKey = $"GetWords_{userId}_WordId_{wordId}";
            if (!_memoryCache.TryGetValue(cacheKey, out DTO.Word? result))
            {
                var userWord = await _dbContext.UserWords
                    .Where(uw => uw.UserId == userId && uw.WordId == wordId)
                    .SingleOrDefaultAsync();

                if (userWord != null)
                {
                    var sqlWord = await _dbContext.Words
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
                        result = WordMapper.ToDto(sqlWord);
                    }

                    _memoryCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                }
            }
            return ServiceResult.Ok(result);
        }

        public async Task<ServiceResult> AddWordAsync(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();

            var user = await _dbContext.Users
                .Include(u => u.UserWords)
                .SingleOrDefaultAsync(x => x.Id == userId);

            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            var existingUserWord = await _dbContext.UserWords
                .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == wordId);

            if (existingUserWord != null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word has already been added.");
            }

            _logger.LogInformation("User: {userId}, Adding personal word: {wordId}", userId, wordId);

            await _dbContext.UserWords.AddAsync(new UserWord
            {
                UserId = userId!,
                WordId = wordId,
                DateAdded = DateTime.UtcNow
            });

            user.LastUpdated = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            _memoryCache.ClearCacheByContains($"GetWords_{userId}");

            return ServiceResult.Ok(user);
        }

        public async Task<ServiceResult> DeleteWordAsync(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _userService.GetUserId();

            var userWord = await _dbContext.UserWords
                .SingleOrDefaultAsync(uw => uw.UserId == userId && uw.WordId == wordId);

            if (userWord == null)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound, "The word ID was not found in your collection.");
            }

            _logger.LogInformation("User: {userId}, Deleting personal word: {wordId}", userId, wordId);

            _dbContext.UserWords.Remove(userWord);

            var user = await _dbContext.Users.FindAsync(userId);
            if (user != null)
            {
                user.LastUpdated = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();

            _memoryCache.ClearCacheByContains($"GetWords_{userId}");

            return ServiceResult.Ok();
        }
    }
}
