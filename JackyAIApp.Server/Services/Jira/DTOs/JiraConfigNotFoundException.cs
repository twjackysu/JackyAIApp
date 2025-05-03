namespace JackyAIApp.Server.Services.Jira.DTOs
{
    public class JiraConfigNotFoundException : Exception
    {
        public JiraConfigNotFoundException()
        {
        }

        public JiraConfigNotFoundException(string message)
            : base(message)
        {
        }

        public JiraConfigNotFoundException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
