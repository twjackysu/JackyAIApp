namespace JackyAIApp.Server.Data.Models
{
    public class JiraConfigBase
    {
        public required string Domain { get; set; }
        public required string Email { get; set; }
        public required string Token { get; set; }
    }
    public class JiraConfig: JiraConfigBase
    {
        public required string Id { get; set; }
    }
}
