using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One row per user's subscription lifecycle (1:1 with User). Independent table rather than
    /// columns on User — a subscription has enough fields plus an audit trail (started/cancelled
    /// timestamps, provider trade number for the cancel API) that it doesn't fit the flat-column
    /// pattern the gamification fields on User use.
    ///
    /// The monthly usage quota itself is NOT tracked here — it's granted via
    /// ICreditService.GrantMonthlyQuotaAsync, which resets User.CreditBalance (the existing
    /// credit-metering engine is reused as-is; a subscription is just "what resets the balance
    /// every period, and to how much").
    /// </summary>
    public class Subscription
    {
        [Key]
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>Current lifecycle state — see <see cref="SubscriptionStatus"/>.</summary>
        public required string Status { get; set; }

        /// <summary>Payment provider that owns this subscription. Currently only "ecpay" supports
        /// recurring billing; PayPal stays one-time-purchase only (see CreditPacks).</summary>
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
