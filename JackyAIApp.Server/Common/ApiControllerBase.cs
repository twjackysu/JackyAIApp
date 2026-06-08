using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Base class for API controllers, providing a single place to resolve the
    /// current authenticated user's id from the request claims.
    /// </summary>
    public abstract class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// The current user's id (Google OAuth NameIdentifier claim), or null when unauthenticated.
        /// </summary>
        protected string? CurrentUserId =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
