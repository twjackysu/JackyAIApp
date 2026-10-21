using JackyAIApp.Server.Common;

namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Gates AI-powered features behind a per-user, per-UTC-day weighted usage budget — the
    /// replacement for per-feature credit consumption (DECISION-006/007). See
    /// <c>.agent/architecture/ARCHITECTURE.md</c> "Credits &amp; Payments 移除設計".
    /// </summary>
    public interface IUsageLimitService
    {
        /// <summary>
        /// Atomically consumes <paramref name="cost"/> units of today's (UTC) budget for
        /// <paramref name="userId"/> if, and only if, doing so would not exceed their effective
        /// daily limit (<c>UsageLimits:DailyBudget</c> × <see cref="Data.Models.SQL.User.UsageLimitMultiplier"/>).
        /// Charge by feature (<see cref="UsageLimitServiceExtensions.TryConsumeAsync"/>) unless the
        /// cost depends on the request, as a chat message's does.
        /// </summary>
        /// <param name="userId">The user charged.</param>
        /// <param name="feature">What the charge is for; logged, and kept for a possible refund —
        /// usage is not tracked per feature.</param>
        /// <param name="cost">How much this use costs.</param>
        /// <returns><c>true</c> if the charge was applied; <c>false</c> if it would have exceeded
        /// the limit (nothing is charged in that case).</returns>
        Task<bool> TryConsumeAsync(string userId, UsageFeature feature, ulong cost);

        /// <summary>
        /// Refunds <paramref name="cost"/> units back to today's (UTC) usage for
        /// <paramref name="userId"/> — e.g. when a charged request failed to deliver a result.
        /// Clamped at zero: never drives <c>Used</c> negative, regardless of how many times it is
        /// called or whether today's row exists at all.
        /// </summary>
        Task RefundAsync(string userId, UsageFeature feature, ulong cost);
    }

    public static class UsageLimitServiceExtensions
    {
        /// <summary>Charges one use of <paramref name="feature"/> at its cost in <see cref="UsageCosts.Of"/>.</summary>
        public static Task<bool> TryConsumeAsync(this IUsageLimitService usageLimits, string userId, UsageFeature feature) =>
            usageLimits.TryConsumeAsync(userId, feature, UsageCosts.Of(feature));
    }
}
