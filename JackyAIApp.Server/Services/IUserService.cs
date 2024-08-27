namespace JackyAIApp.Server.Services
{
    public interface IUserService
    {
        string? GetUserId();
        string? GetUserName();
        string? GetUserEmail();
        string? GetIssuer();
    }
}
