namespace JackyAIApp.Server.Services
{
    public interface IPayUniService
    {
        /// <summary>
        /// Creates a PayUni UPP (整合式支付頁) checkout form for one month of the Pro plan.
        /// PayUni has no recurring-billing API (unlike ECPay's 信用卡定期定額) — every month is a
        /// separate manually-triggered payment; there is nothing to auto-renew.
        /// </summary>
        Task<Dictionary<string, string>> CreateSubscriptionPaymentAsync(string userId);

        /// <summary>
        /// Handles PayUni's NotifyURL — the authoritative server-to-server payment result. Verifies
        /// HashInfo, decrypts EncryptInfo, and grants a month of quota on TradeStatus == "1".
        /// </summary>
        Task<bool> HandleNotifyCallbackAsync(IFormCollection form);

        /// <summary>
        /// Handles PayUni's ReturnURL — the browser-facing redirect after checkout. Same
        /// verify+decrypt+grant as the notify callback (idempotent via PayUni's per-transaction
        /// TradeNo, so it's safe to run this in addition to the notify handler — it just makes the
        /// grant visible to the user faster than waiting on the async notify).
        /// </summary>
        Task<bool> HandleReturnCallbackAsync(IFormCollection form);

        /// <summary>
        /// Marks the local subscription as cancelled. There is no PayUni API to call — nothing is
        /// scheduled to auto-charge next month, so "cancelling" just means the user won't manually
        /// pay again. Access remains until CurrentPeriodEnd (already paid for).
        /// </summary>
        Task<bool> CancelSubscriptionAsync(string userId);

        /// <summary>
        /// Reads the user's current subscription state for display (status, renewal date, quota).
        /// </summary>
        Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(string userId);
    }
}
