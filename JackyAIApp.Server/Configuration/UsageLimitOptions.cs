namespace JackyAIApp.Server.Configuration
{
    /// <summary>
    /// Settings for the daily usage-limit budget that replaces per-feature credit charges
    /// (DECISION-006/007). See <see cref="Services.IUsageLimitService"/>.
    /// </summary>
    public class UsageLimitOptions
    {
        public const string SectionName = "UsageLimits";

        /// <summary>
        /// Default daily budget (UTC day) shared across all AI-powered features, weighted by
        /// <see cref="Common.UsageCosts"/>. Roughly equivalent to the old credit system's typical
        /// daily allowance. Adjustable without a code change; a per-user override is
        /// <see cref="Data.Models.SQL.User.UsageLimitMultiplier"/>.
        /// </summary>
        public ulong DailyBudget { get; set; } = 60;
    }
}
