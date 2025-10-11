namespace JackyAIApp.Server.Services
{
    public interface IMicrosoftGraphService
    {
        Task<object> GetUserEmailsAsync();
        Task<object> GetUserCalendarEventsAsync();
        Task<object> GetUserTeamsAsync();
        Task<object> GetUserChatsAsync();
        Task<object> GetUserProfileAsync();
    }
}