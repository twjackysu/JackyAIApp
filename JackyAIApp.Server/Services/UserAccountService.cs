using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;
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

        /// <summary>What the client needs about the signed-in user; <see cref="UserAccountFailures.Missing"/>
        /// when their row is not there.</summary>
        Task<ServiceResult<AccountInfoResponse>> GetInfoAsync(CancellationToken ct);
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

        public async Task<ServiceResult<AccountInfoResponse>> GetInfoAsync(CancellationToken ct)
        {
            var userId = currentUser.RequireId();
            var user = await dbContext.Users
                .Where(x => x.Id == userId)
                .Select(x => new { x.Id, x.Name, x.Email, x.IsAdmin })
                .SingleOrDefaultAsync(ct);
            if (user == null)
                return UserAccountFailures.Missing;

            return ServiceResult.Ok(new AccountInfoResponse(
                user.Id, user.Name, user.Email, user.IsAdmin == true, drawdownBrakeAccess.IsEnabledFor(user.IsAdmin)));
        }
    }
}
