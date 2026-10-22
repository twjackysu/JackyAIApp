using System.Linq.Expressions;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Services.DataAnalysis
{
    /// <summary>
    /// Who may see and change a dashboard — the one place these rules are written. The SQL filters
    /// (<see cref="OwnedBy"/>, <see cref="ListedFor"/>) are expressions so EF runs them in the
    /// query; <see cref="CanView"/> checks a dashboard already loaded, with its Shares.
    /// </summary>
    public static class DashboardAccessPolicy
    {
        /// <summary>Only the owner changes a dashboard, or sees its sharing settings.</summary>
        public static Expression<Func<Dashboard, bool>> OwnedBy(string userId) => d => d.UserId == userId;

        /// <summary>
        /// The dashboards in a user's own list: theirs, and the Restricted ones shared with them.
        /// Someone else's Public or Anyone dashboard is reached by its link, not listed. Every
        /// dashboard listed is one <see cref="CanView"/> allows.
        /// </summary>
        public static Expression<Func<Dashboard, bool>> ListedFor(string userId) => d =>
            d.UserId == userId ||
            (d.Visibility == DashboardVisibility.Restricted && d.Shares.Any(s => s.UserId == userId));

        /// <summary>Whether a (possibly anonymous) viewer may see the dashboard. Needs its Shares loaded.</summary>
        public static bool CanView(Dashboard dashboard, string? userId) =>
            dashboard.Visibility == DashboardVisibility.Anyone ||
            (userId != null && (
                dashboard.UserId == userId ||
                dashboard.Visibility == DashboardVisibility.Public ||
                (dashboard.Visibility == DashboardVisibility.Restricted && dashboard.Shares.Any(s => s.UserId == userId))));

        /// <summary>A visibility as the API sends it: exactly one of the names, so neither a number
        /// nor another casing gets through.</summary>
        public static bool TryParseVisibility(string? value, out DashboardVisibility visibility)
        {
            visibility = default;
            return value != null
                && Enum.GetNames<DashboardVisibility>().Contains(value, StringComparer.Ordinal)
                && Enum.TryParse(value, out visibility);
        }
    }
}
