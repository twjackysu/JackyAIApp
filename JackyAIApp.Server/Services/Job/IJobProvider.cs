using JackyAIApp.Server.DTO.Job;

namespace JackyAIApp.Server.Services.Job;

public interface IJobProvider
{
    string PlatformId { get; }
    Task<IReadOnlyList<JobListingDto>> FetchAsync(string keywords, string searchStrategy, CancellationToken ct = default);
}
