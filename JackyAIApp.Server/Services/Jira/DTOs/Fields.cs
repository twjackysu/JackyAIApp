namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class Fields
    {
        public required string Summary { get; set; }
        public required string Description { get; set; }
        public required List<string> Labels { get; set; }
    }
}
