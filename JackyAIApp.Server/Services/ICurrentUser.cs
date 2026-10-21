using System.Security.Claims;
using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Who the current request is made by, read from the sign-in cookie's claims. The one way the
    /// app asks — controllers and services alike.
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>The signed-in user's id (Google's NameIdentifier), or null for an anonymous request.
        /// Behind <c>[Authorize]</c>, use <see cref="CurrentUserExtensions.RequireId"/> instead.</summary>
        string? Id { get; }

        string? Name { get; }

        string? Email { get; }
    }

    public class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

        public string? Id => Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        public string? Name => Principal?.FindFirst(ClaimTypes.Name)?.Value;

        public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;
    }

    public static class CurrentUserExtensions
    {
        /// <summary>
        /// The signed-in user's id, for code that only runs for a signed-in user (behind
        /// <c>[Authorize]</c>, so it is always there). Throws <see cref="NotSignedInException"/>
        /// otherwise, which is answered as 401 — so there is no null check to write.
        /// </summary>
        public static string RequireId(this ICurrentUser user) =>
            string.IsNullOrEmpty(user.Id) ? throw new NotSignedInException() : user.Id;
    }

    /// <summary>A signed-in user was required and there is none; <see cref="ExceptionMiddleware"/>
    /// answers it as 401 + <see cref="ErrorCodes.Unauthorized"/>.</summary>
    public sealed class NotSignedInException() : Exception("Not signed in.");

    public static class UserAccountFailures
    {
        /// <summary>
        /// The sign-in cookie is valid but the user's row is gone. 401, not 403: signing in again
        /// recreates the row (<c>AccountController</c>'s sign-in callback), so that is the way out.
        /// </summary>
        public static ServiceFailure Missing { get; } =
            ServiceResult.Fail(ErrorCodes.Unauthorized, "Your account was not found. Please sign in again.");
    }
}
