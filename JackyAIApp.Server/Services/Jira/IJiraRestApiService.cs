using JackyAIApp.Server.Services.Jira.DTOs;

namespace JackyAIApp.Server.Services.Jira
{
    public interface IJiraRestApiService
    {
        Task<JiraSearchResponse?> SearchAsync(string domain, string email, string token, string jql);
    }
}
