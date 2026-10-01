using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Unused since the Credit/Payment removal (DECISION-006/007, TASK-023) — subscriptions and
    /// the credit-metering engine this entity depended on (<c>ICreditService</c>) are gone.
    /// Entity and table kept so no data is lost; removal deferred to TASK-024 (needs Boss
    /// sign-off for the irreversible table DROP).
    /// </summary>
    public class Subscription
    {
        [Key]
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>Current lifecycle state — see <see cref="SubscriptionStatus"/>.</summary>
        public required string Status { get; set; }

        /// <summary>Payment provider that owned this subscription. Only "ecpay" supported
        /// recurring billing; PayPal was one-time-purchase only.</summary>
        public string? Provider { get; set; }

        /// <summary>End of the period the last successful charge paid for. A period grant resets
        /// this to (grant time + 1 month); once it passes with no new successful charge, the
        /// subscription is effectively lapsed even if Status hasn't been explicitly flipped yet
        /// (e.g. ECPay's own retry window before it gives up and the merchant sees no more
        /// PeriodReturnURL calls).</summary>
        public DateTime? CurrentPeriodEnd { get; set; }

        /// <summary>ECPay's MerchantTradeNo from order creation — required to call the
        /// CreditCardPeriodAction cancel API later (it identifies which periodic order to
        /// terminate).</summary>
        public string? EcpayMerchantTradeNo { get; set; }

        /// <summary>PayUni's MerTradeNo from the most recent successful UPP checkout. PayUni has
        /// no recurring-billing engine (unlike ECPay's 信用卡定期定額) — each month is a separate
        /// manually-triggered UPP payment, so this is kept for trade lookups/support only, not
        /// for any cancel/renewal API call.</summary>
        public string? PayUniMerTradeNo { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public static class SubscriptionStatus
    {
        /// <summary>Paying, most recent period charge succeeded.</summary>
        public const string Active = "active";

        /// <summary>User cancelled — access continues until CurrentPeriodEnd (already paid for),
        /// then no further monthly grants happen.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>CurrentPeriodEnd has passed with no renewal (cancelled-and-lapsed, or a
        /// period charge failed and ECPay gave up retrying).</summary>
        public const string Expired = "expired";
    }
}
