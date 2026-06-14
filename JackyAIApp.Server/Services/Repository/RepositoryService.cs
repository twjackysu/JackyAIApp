using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Mappers;
using JackyAIApp.Server.Services.Dictionary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Repository
{
    public class RepositoryService(
        ILogger<RepositoryService> logger,
        AzureSQLDBContext dbContext,
        IUserService userService,
        IDictionaryService dictionaryService,
        IExtendedMemoryCache memoryCache) : IRepositoryService
    {
        private readonly ILogger<RepositoryService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IUserService _userService = userService;
        private readonly IDictionaryService _dictionaryService = dictionaryService;
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

        public ServiceResult GetWordPacks()
        {
            var packs = WordPacks.All
                .Select(p => new DTO.WordPackDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    WordCount = p.Words.Count,
                })
                .ToList();
            return ServiceResult.Ok(packs);
        }

        public async Task<ServiceResult> AddWordPackAsync(string packId)
        {
            var pack = WordPacks.Find(packId);
            if (pack == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Unknown word pack.");
            }

            var userId = _userService.GetUserId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");
            }

            int added = 0, alreadyHad = 0, failed = 0;

            foreach (var text in pack.Words)
            {
                // Ensure the (shared) dictionary entry exists; generates it via OpenAI if missing. Free.
                var wordResult = await _dictionaryService.GetWordAsync(text);
                if (!wordResult.IsSuccess || wordResult.Data is not DTO.Word dto || string.IsNullOrEmpty(dto.Id))
                {
                    _logger.LogWarning("Word pack {Pack}: could not resolve word '{Word}'", packId, text);
                    failed++;
                    continue;
                }

                var exists = await _dbContext.UserWords
                    .AnyAsync(uw => uw.UserId == userId && uw.WordId == dto.Id);
                if (exists)
                {
                    alreadyHad++;
                    continue;
                }

                await _dbContext.UserWords.AddAsync(new UserWord
                {
                    UserId = userId!,
                    WordId = dto.Id,
                    DateAdded = DateTime.UtcNow,
                });
                added++;
            }

            if (added > 0)
            {
                user.LastUpdated = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                _memoryCache.ClearCacheByContains($"GetWords_{userId}");
            }

            _logger.LogInformation(
                "User {User} added word pack {Pack}: {Added} added, {AlreadyHad} already had, {Failed} failed",
                userId, packId, added, alreadyHad, failed);

            return ServiceResult.Ok(new DTO.AddWordPackResultDto
            {
                Added = added,
                AlreadyHad = alreadyHad,
                Failed = failed,
            });
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
