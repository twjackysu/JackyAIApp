namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class JiraSearchRequest
    {
        public required string Domain { get; set; }
        public required string Email { get; set; }
        public required string Token { get; set; }
        public required string Jql { get; set; }
    }
}
