using JackyAIApp.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    public interface IDrawdownBrakeAccess
    {
        /// <summary>Whether a user with this admin flag may use the 回落煞車 feature.</summary>
        bool IsEnabledFor(bool? isAdmin);

        /// <summary>The same, looking the user's admin flag up.</summary>
        Task<bool> IsEnabledForUserAsync(string userId, CancellationToken ct = default);
    }

    /// <summary>
    /// Who sees 回落煞車, until the Boss decides to open it to everyone (Issue #431, Q1): app
    /// setting <c>Features:DrawdownBrake:Audience</c> — <c>Admin</c> (the default) or <c>All</c>.
    /// Changing it is an App Setting edit, not a deploy. Anything else, a typo included, means
    /// Admin: a mistyped value must never open the feature up.
    /// </summary>
    public class DrawdownBrakeAccess(IConfiguration configuration, IDbContextFactory<AzureSQLDBContext> dbFactory)
        : IDrawdownBrakeAccess
    {
        internal const string AudienceKey = "Features:DrawdownBrake:Audience";

        public bool IsEnabledFor(bool? isAdmin) =>
            isAdmin == true
            || string.Equals(configuration[AudienceKey]?.Trim(), "All", StringComparison.OrdinalIgnoreCase);

        public async Task<bool> IsEnabledForUserAsync(string userId, CancellationToken ct = default)
        {
            // Open to everyone: no need to look the user up.
            if (IsEnabledFor(false)) return true;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var isAdmin = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.IsAdmin)
                .FirstOrDefaultAsync(ct);
            return IsEnabledFor(isAdmin);
        }
    }
}
