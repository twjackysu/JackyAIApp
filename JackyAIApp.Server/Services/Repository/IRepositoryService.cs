using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Repository
{
    /// <summary>Personal word-collection operations for the current user.</summary>
    public interface IRepositoryService
    {
        Task<ServiceResult<List<DTO.Word>>> GetWordsAsync(int pageNumber, int pageSize);
        /// <summary>The word, if it is in the user's collection; null (still a success) when it is not.</summary>
        Task<ServiceResult<DTO.Word?>> GetWordAsync(string wordId);
        Task<ServiceResult<NoData>> AddWordAsync(string wordId);
        Task<ServiceResult<NoData>> DeleteWordAsync(string wordId);

        /// <summary>
        /// The words the user added most recently (newest first, at most <see cref="SavedWords.MaxRecent"/>), each with
        /// its first meaning's part of speech and Chinese, and how many the collection holds in all.
        /// </summary>
        Task<ServiceResult<SavedWords>> GetRecentWordsAsync(int count, CancellationToken ct = default);

        /// <summary>Lists the built-in starter word packs (metadata only).</summary>
        ServiceResult<List<DTO.WordPackDto>> GetWordPacks();

        /// <summary>
        /// Adds every word in a starter pack to the current user's repository, generating any
        /// missing dictionary entries first. Idempotent: words already saved are skipped. Free.
        /// </summary>
        Task<ServiceResult<DTO.AddWordPackResultDto>> AddWordPackAsync(string packId);
    }
}
