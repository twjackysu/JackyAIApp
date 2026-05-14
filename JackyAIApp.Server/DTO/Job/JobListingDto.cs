namespace JackyAIApp.Server.DTO.Job;

public record JobListingDto(
    string Id,
    string Title,
    string Company,
    string Location,
    string? SalaryRange,
    string Url,
    DateTime PostedAt,
    string Platform,
    string[] Tags,
    string? Description
);

public record JobSearchRequest(
    string Keywords,
    string SearchStrategy,   // "AND" | "OR"
    string[] Platforms       // ["104", "github-f2etw", "github-golangtw", "github-devopstw"]
);
