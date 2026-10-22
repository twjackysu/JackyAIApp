using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>
    /// Dictionary domain operations: looking up (and lazily generating) word
    /// definitions, and invalidating cached/stored definitions.
    /// Results carry a <see cref="DTO.Word"/> payload on success.
    /// </summary>
    public interface IDictionaryService
    {
        /// <summary>
        /// Returns the definition for <paramref name="word"/>, generating it via
        /// OpenAI and persisting it when not already cached/stored (see <see cref="IWordGenerator"/>).
        /// <paramref name="ct"/> stops the lookup, not a generation it waits on: that one is stored anyway.
        /// </summary>
        Task<ServiceResult<DTO.Word>> GetWordAsync(string word, CancellationToken ct = default);

        /// <summary>Marks a stored word as invalid so it will be regenerated next lookup.</summary>
        Task<ServiceResult<DTO.Word>> InvalidateWordAsync(string word);

        /// <summary>
        /// Returns Hunspell spelling suggestions for a word, for when a lookup fails
        /// dictionary validation (typo recovery). Does not call OpenAI.
        /// </summary>
        ServiceResult<WordSuggestionsResponse> SuggestWords(string word);
    }
}
