using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.DailyChallenge
{
    /// <summary>Daily challenge flows: fetch, submit, stats.</summary>
    public interface IDailyChallengeService
    {
        Task<ServiceResult> GetChallengeAsync();
        Task<ServiceResult> SubmitChallengeAsync(DailyChallengeSubmitRequest request);
        Task<ServiceResult> GetStatsAsync();
    }
}
