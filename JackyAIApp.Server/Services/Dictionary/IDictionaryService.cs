using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Dictionary
{
    /// <summary>
    /// Result of a dictionary operation: either a word DTO on success,
    /// or an error code + message on failure.
    /// </summary>
    public sealed record DictionaryResult(DTO.Word? Word, ErrorCodes? Error, string? ErrorMessage)
    {
        public bool IsSuccess => Error is null;

        public static DictionaryResult Ok(DTO.Word word) => new(word, null, null);

        public static DictionaryResult Fail(ErrorCodes error, string message) => new(null, error, message);
    }

    /// <summary>
    /// Dictionary domain operations: looking up (and lazily generating) word
    /// definitions, and invalidating cached/stored definitions.
    /// </summary>
    public interface IDictionaryService
    {
        /// <summary>
        /// Returns the definition for <paramref name="word"/>, generating it via
        /// OpenAI and persisting it when not already cached/stored.
        /// </summary>
        Task<DictionaryResult> GetWordAsync(string word);

        /// <summary>Marks a stored word as invalid so it will be regenerated next lookup.</summary>
        Task<DictionaryResult> InvalidateWordAsync(string word);
    }
}
