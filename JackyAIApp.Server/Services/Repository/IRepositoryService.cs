using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services.Repository
{
    /// <summary>Personal word-collection operations for the current user.</summary>
    public interface IRepositoryService
    {
        Task<ServiceResult> GetWordsAsync(int pageNumber, int pageSize);
        Task<ServiceResult> GetWordAsync(string wordId);
        Task<ServiceResult> AddWordAsync(string wordId);
        Task<ServiceResult> DeleteWordAsync(string wordId);

        /// <summary>Lists the built-in starter word packs (metadata only).</summary>
        ServiceResult GetWordPacks();

        /// <summary>
        /// Adds every word in a starter pack to the current user's repository, generating any
        /// missing dictionary entries first. Idempotent: words already saved are skipped. Free.
        /// </summary>
        Task<ServiceResult> AddWordPackAsync(string packId);
    }
}
