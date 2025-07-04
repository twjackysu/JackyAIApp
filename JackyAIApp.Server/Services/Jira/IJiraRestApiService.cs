﻿
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Jira.DTOs;

namespace JackyAIApp.Server.Services.Jira
{
    public interface IJiraRestApiService
    {
        Task<JiraSearchResponse?> SearchAsync(string jiraConfigId, string jql);
        Task<IEnumerable<DTO.JiraConfig>> GetJiraConfigs();
        Task<string> AddJiraConfig(string domain, string email, string token);
        Task DeleteJiraConfig(string jiraConfigId);
    }
}
