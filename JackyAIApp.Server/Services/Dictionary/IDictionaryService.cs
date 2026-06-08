using JackyAIApp.Server.Common;

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
        /// OpenAI and persisting it when not already cached/stored.
        /// </summary>
        Task<ServiceResult> GetWordAsync(string word);

        /// <summary>Marks a stored word as invalid so it will be regenerated next lookup.</summary>
        Task<ServiceResult> InvalidateWordAsync(string word);
    }
}
