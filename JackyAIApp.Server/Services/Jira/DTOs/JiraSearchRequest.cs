namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class JiraSearchRequest
    {
        public required string JiraConfigId { get; set; }
        public required string Jql { get; set; }
    }
}
