namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class JiraIssue
    {
        public required string Key { get; set; }
        public required Fields Fields { get; set; }
    }
}
