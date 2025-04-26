namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class JiraSearchResponse
    {
        public required List<JiraIssue> Issues { get; set; }
    }
}
