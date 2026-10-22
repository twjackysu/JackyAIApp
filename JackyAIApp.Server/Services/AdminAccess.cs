using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Whether the signed-in user is an admin (<c>Users.IsAdmin</c>, set by hand in the database).
    /// An endpoint that only admins may call takes <see cref="Common.AdminOnlyAttribute"/> instead of
    /// asking this itself.
    /// </summary>
    public interface IAdminAccess
    {
        /// <summary>False for an anonymous request and for a user whose row is missing.</summary>
        Task<bool> IsAdminAsync(CancellationToken ct);
    }

    public class AdminAccess(AzureSQLDBContext dbContext, ICurrentUser currentUser) : IAdminAccess
    {
        public async Task<bool> IsAdminAsync(CancellationToken ct)
        {
            var userId = currentUser.Id;
            return !string.IsNullOrEmpty(userId) &&
                await dbContext.Users.AnyAsync(u => u.Id == userId && u.IsAdmin == true, ct);
        }
    }
}
