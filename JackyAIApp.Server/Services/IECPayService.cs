namespace JackyAIApp.Server.Services
{
    public interface IECPayService
    {
        /// <summary>
        /// Creates ECPay payment form data (to POST to ECPay gateway).
        /// </summary>
        Task<Dictionary<string, string>> CreatePaymentAsync(string userId, string packId);

        /// <summary>
        /// Handles ECPay callback, verifies CheckMacValue, and adds credits.
        /// </summary>
        Task<bool> HandleCallbackAsync(IFormCollection form);

        /// <summary>
        /// Creates an ECPay recurring (信用卡定期定額) payment form for the Pro subscription.
        /// </summary>
        Task<Dictionary<string, string>> CreateSubscriptionPaymentAsync(string userId);

        /// <summary>
        /// Handles ECPay's ReturnURL callback for a subscription's first charge.
        /// </summary>
        Task<bool> HandleSubscriptionFirstChargeCallbackAsync(IFormCollection form);

        /// <summary>
        /// Handles ECPay's PeriodReturnURL callback for a subscription's 2nd+ period charge.
        /// </summary>
        Task<bool> HandleSubscriptionPeriodCallbackAsync(IFormCollection form);

        /// <summary>
        /// Calls ECPay's CreditCardPeriodAction (Action=Cancel) to terminate the user's recurring
        /// order, then marks the local Subscription as cancelled. Access remains until
        /// CurrentPeriodEnd (already paid for) — this only stops future charges.
        /// </summary>
        Task<bool> CancelSubscriptionAsync(string userId);

        /// <summary>
        /// Reads the user's current subscription state for display (status, renewal date, quota).
        /// </summary>
        Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(string userId);
    }

    public class SubscriptionStatusResult
    {
        public bool IsSubscribed { get; set; }
        public string? Status { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public ulong MonthlyQuota { get; set; }
        public ulong CurrentBalance { get; set; }
    }
}
