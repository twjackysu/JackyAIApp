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
    }
}
