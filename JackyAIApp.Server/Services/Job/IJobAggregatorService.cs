using JackyAIApp.Server.DTO.Job;

namespace JackyAIApp.Server.Services.Job;

public interface IJobAggregatorService
{
    Task<IReadOnlyList<JobListingDto>> SearchAsync(JobSearchRequest request, CancellationToken ct = default);
}
