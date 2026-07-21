namespace JackyAIApp.Server.Services
{
    /// <summary>
    /// Single Pro tier for now — no per-tier table, just constants. Add a real plan lookup if a
    /// second tier ever ships.
    /// </summary>
    public static class SubscriptionPlans
    {
        public const string ProId = "pro";
        public const string ProName = "Pro 訂閱方案";
        public const int ProPriceTWD = 150;

        /// <summary>Reset (not additive) every billing period — see
        /// ICreditService.GrantMonthlyQuotaAsync. Sized well above realistic heavy-user usage;
        /// see .claude/plans/subscription-model.md for the cost math behind this number.</summary>
        public const ulong ProMonthlyQuota = 3000;
    }
}
