using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Mappers;
using JackyAIApp.Server.Services.SpellChecker;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using SQL = JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.Dictionary
{
    public class DictionaryService(
        AzureSQLDBContext dbContext,
        IWordGenerator wordGenerator,
        IMemoryCache memoryCache,
        ISpellChecker spellChecker,
        TimeProvider? timeProvider = null) : IDictionaryService
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly IWordGenerator _wordGenerator = wordGenerator;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly ISpellChecker _spellChecker = spellChecker;
        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        private static string CacheKey(string lowerWord) => $"Get_Dictionary_{lowerWord}";

        public async Task<ServiceResult<DTO.Word>> GetWordAsync(string word, CancellationToken ct = default)
        {
            var lowerWord = word.Trim().ToLower();

            // Validate word against dictionary (shared singleton checker)
            if (!_spellChecker.Check(lowerWord))
            {
                return ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "This is not a valid word.");
            }

            // A regular inflection is shown on its root's entry, as dictionaries do: "establishing" on
            // "establish". Irregular forms ("went") and forms the word list holds on their own ("studied",
            // often with a meaning of their own) keep entries of their own.
            var root = _spellChecker.RootOf(lowerWord);
            var entryWord = root ?? lowerWord;
            var inflectedForm = root == null ? null : lowerWord;

            var cacheKey = CacheKey(entryWord);
            SQL.Word? dbWord;

            if (_memoryCache.TryGetValue(cacheKey, out object? cachedWord) && cachedWord is SQL.Word cachedDbWord)
            {
                dbWord = cachedDbWord;
            }
            else
            {
                dbWord = await WordQueries.LoadAsync(_dbContext, entryWord, ct);
                _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));
            }

            // Return cached/DB result if valid
            if (dbWord != null && (!dbWord.DataInvalid.HasValue || !dbWord.DataInvalid.Value))
            {
                return ServiceResult.Ok(ToDto(dbWord, inflectedForm));
            }

            // Generated once and stored, however many lookups wait on it — and whether or not this one
            // still does when it is done.
            dbWord = await _wordGenerator.GenerateAsync(entryWord, ct);
            if (dbWord == null)
            {
                return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed,
                    "Query failed, OpenAI could not generate the corresponding word.");
            }

            _memoryCache.Set(cacheKey, dbWord, TimeSpan.FromDays(1));

            return ServiceResult.Ok(ToDto(dbWord, inflectedForm));
        }

        private static DTO.Word ToDto(SQL.Word dbWord, string? inflectedForm)
        {
            var dto = WordMapper.ToDto(dbWord);
            dto.InflectedForm = inflectedForm;
            return dto;
        }

        public async Task<ServiceResult<DTO.Word>> InvalidateWordAsync(string word)
        {
            var lowerWord = word.Trim().ToLower();
            _memoryCache.Remove(CacheKey(lowerWord));

            var result = await WordQueries.LoadAsync(_dbContext, lowerWord, CancellationToken.None);
            if (result == null)
            {
                return ServiceResult.Fail(ErrorCodes.TheWordCannotBeFound, "Word not found");
            }

            result.DataInvalid = true;
            result.LastUpdated = _time.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync();

            return ServiceResult.Ok(WordMapper.ToDto(result));
        }

        public ServiceResult<WordSuggestionsResponse> SuggestWords(string word)
        {
            var lowerWord = word.Trim().ToLower();
            if (string.IsNullOrEmpty(lowerWord))
            {
                return ServiceResult.Ok(new WordSuggestionsResponse { Suggestions = new List<string>() });
            }

            var suggestions = _spellChecker.Suggest(lowerWord);
            return ServiceResult.Ok(new WordSuggestionsResponse { Suggestions = suggestions.ToList() });
        }
    }
}
