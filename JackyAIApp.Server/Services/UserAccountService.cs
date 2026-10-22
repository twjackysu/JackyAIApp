using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.Services.Portfolio.DrawdownBrake;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// The signed-in user's own <c>Users</c> row: created on their first sign-in, and read back for
    /// the client (<c>GET api/account/info</c>).
    /// </summary>
    public interface IUserAccountService
    {
        /// <summary>Creates the signed-in user's row from their sign-in claims if it does not exist
        /// yet; nothing for an anonymous request.</summary>
        Task EnsureCreatedAsync(CancellationToken ct);

        /// <summary>Whether the signed-in user's row exists. A service that needs the row to act
        /// answers <see cref="UserAccountFailures.Missing"/> when it does not.</summary>
        Task<bool> ExistsAsync(CancellationToken ct = default);

        /// <summary>The signed-in user's row, with <see cref="User.DrawdownBrakeEnabled"/> filled in;
        /// <see cref="UserAccountFailures.Missing"/> when there is none.</summary>
        Task<ServiceResult<User>> GetInfoAsync(CancellationToken ct);
    }

    public class UserAccountService(
        ILogger<UserAccountService> logger,
        AzureSQLDBContext dbContext,
        ICurrentUser currentUser,
        IDrawdownBrakeAccess drawdownBrakeAccess) : IUserAccountService
    {
        public async Task EnsureCreatedAsync(CancellationToken ct)
        {
            var userId = currentUser.Id;
            if (userId == null)
                return;

            if (await dbContext.Users.AnyAsync(x => x.Id == userId, ct))
                return;

            dbContext.Users.Add(new User
            {
                Id = userId,
                Name = currentUser.Name,
                Email = currentUser.Email,
                LastUpdated = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("New user created: {UserId}", userId);
        }

        public Task<bool> ExistsAsync(CancellationToken ct = default)
        {
            var userId = currentUser.RequireId();
            return dbContext.Users.AnyAsync(x => x.Id == userId, ct);
        }

        public async Task<ServiceResult<User>> GetInfoAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user == null)
                return UserAccountFailures.Missing;

            user.DrawdownBrakeEnabled = drawdownBrakeAccess.IsEnabledFor(user.IsAdmin);
            return ServiceResult.Ok(user);
        }
    }
}
