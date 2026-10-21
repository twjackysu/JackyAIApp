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
        ICurrentUser currentUser,
        IDictionaryService dictionaryService,
        IExtendedMemoryCache memoryCache) : IRepositoryService
    {
        private readonly ILogger<RepositoryService> _logger = logger;
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly IDictionaryService _dictionaryService = dictionaryService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;

        public async Task<ServiceResult<List<DTO.Word>>> GetWordsAsync(int pageNumber, int pageSize)
        {
            var userId = _currentUser.RequireId();
            var cacheKey = $"GetWords_{userId}_Page_{pageNumber}_Size_{pageSize}";
            if (!_memoryCache.TryGetValue(cacheKey, out List<DTO.Word>? result))
            {
                var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
                if (user == null)
                {
                    return UserAccountFailures.Missing;
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
            return ServiceResult.Ok(result ?? []);
        }

        public async Task<ServiceResult<DTO.Word?>> GetWordAsync(string wordId)
        {
            var userId = _currentUser.RequireId();
            var cacheKey = $"GetWords_{userId}_WordId_{wordId}";
            if (!_memoryCache.TryGetValue(cacheKey, out DTO.Word? result))
            {
                var inCollection = await _dbContext.UserWords
                    .AnyAsync(uw => uw.UserId == userId && uw.WordId == wordId);

                if (inCollection)
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

        public async Task<ServiceResult<NoData>> AddWordAsync(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _currentUser.RequireId();

            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);

            if (user == null)
            {
                return UserAccountFailures.Missing;
            }

            var alreadyAdded = await _dbContext.UserWords
                .AnyAsync(uw => uw.UserId == userId && uw.WordId == wordId);

            if (alreadyAdded)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word has already been added.");
            }

            _logger.LogInformation("User: {userId}, Adding personal word: {wordId}", userId, wordId);

            await _dbContext.UserWords.AddAsync(new UserWord
            {
                UserId = userId,
                WordId = wordId,
                DateAdded = DateTime.UtcNow
            });

            user.LastUpdated = DateTime.UtcNow;
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A second add of the same word (a double click) passed the check above too, and
                // saved first; the unique index on (UserId, WordId) rejected this one. The word is
                // in the collection either way — answer as if the check had caught it.
                _dbContext.ChangeTracker.Clear();
                if (!await _dbContext.UserWords.AnyAsync(uw => uw.UserId == userId && uw.WordId == wordId))
                    throw;
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word has already been added.");
            }

            _memoryCache.ClearCacheByContains($"GetWords_{userId}");

            // Nothing to return: the client only needs to know it worked (it used to get the whole
            // User entity back, its saved words and internal fields included).
            return ServiceResult.Ok();
        }

        public ServiceResult<List<DTO.WordPackDto>> GetWordPacks()
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

        public async Task<ServiceResult<DTO.AddWordPackResultDto>> AddWordPackAsync(string packId)
        {
            var pack = WordPacks.Find(packId);
            if (pack == null)
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Unknown word pack.");
            }

            var userId = _currentUser.RequireId();
            var user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId);
            if (user == null)
            {
                return UserAccountFailures.Missing;
            }

            int added = 0, alreadyHad = 0, failed = 0;
            var pending = new List<string>();

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

                // pending: two words in the pack resolving to the same dictionary entry must not be
                // added twice either.
                var exists = pending.Contains(dto.Id) || await _dbContext.UserWords
                    .AnyAsync(uw => uw.UserId == userId && uw.WordId == dto.Id);
                if (exists)
                {
                    alreadyHad++;
                    continue;
                }

                await _dbContext.UserWords.AddAsync(new UserWord
                {
                    UserId = userId,
                    WordId = dto.Id,
                    DateAdded = DateTime.UtcNow,
                });
                pending.Add(dto.Id);
            }

            if (pending.Count > 0)
            {
                user.LastUpdated = DateTime.UtcNow;
                try
                {
                    await _dbContext.SaveChangesAsync();
                    added = pending.Count;
                }
                catch (DbUpdateException)
                {
                    // Another request (the same pack added twice) saved some of these words between
                    // the checks above and this save, and the unique index on (UserId, WordId)
                    // rejected the whole batch. Add whatever is still missing.
                    _dbContext.ChangeTracker.Clear();
                    var nowInCollection = await _dbContext.UserWords
                        .Where(uw => uw.UserId == userId && pending.Contains(uw.WordId))
                        .Select(uw => uw.WordId)
                        .ToListAsync();
                    var stillMissing = pending.Except(nowInCollection).ToList();
                    if (stillMissing.Count == pending.Count)
                        throw; // none of them appeared meanwhile, so the failure is something else

                    foreach (var wordId in stillMissing)
                    {
                        await _dbContext.UserWords.AddAsync(new UserWord
                        {
                            UserId = userId,
                            WordId = wordId,
                            DateAdded = DateTime.UtcNow,
                        });
                    }
                    (await _dbContext.Users.SingleAsync(x => x.Id == userId)).LastUpdated = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    added = stillMissing.Count;
                    alreadyHad += pending.Count - stillMissing.Count;
                }
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

        public async Task<ServiceResult<NoData>> DeleteWordAsync(string wordId)
        {
            if (string.IsNullOrEmpty(wordId))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "The word ID is invalid.");
            }

            var userId = _currentUser.RequireId();

            // Every matching row, not SingleOrDefault: that threw on the duplicates a double click
            // could leave before (UserId, WordId) was unique, so such a word could never be removed.
            var userWords = await _dbContext.UserWords
                .Where(uw => uw.UserId == userId && uw.WordId == wordId)
                .ToListAsync();

            if (userWords.Count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.NotFound, "The word ID was not found in your collection.");
            }

            _logger.LogInformation("User: {userId}, Deleting personal word: {wordId}", userId, wordId);

            _dbContext.UserWords.RemoveRange(userWords);

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
