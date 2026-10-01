using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Unused since the Credit/Payment removal (DECISION-006/007, TASK-023) — nothing writes new
    /// rows any more. Entity and table kept so historical data isn't lost; removal deferred to
    /// TASK-024 (needs Boss sign-off for the irreversible table DROP).
    /// </summary>
    public class CreditTransaction
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// Foreign key to the User table
        /// </summary>
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>
        /// Amount of credits (positive = add, negative = consume)
        /// </summary>
        public required long Amount { get; set; }

        /// <summary>
        /// Balance after this transaction
        /// </summary>
        public required ulong BalanceAfter { get; set; }

        /// <summary>
        /// Type of transaction: consume, topup, refund, bonus, initial
        /// </summary>
        public required string TransactionType { get; set; }

        /// <summary>
        /// Reason for the transaction (e.g., "stock_analysis", "tts_generation", "stripe_payment")
        /// </summary>
        public required string Reason { get; set; }

        /// <summary>
        /// Optional description or notes
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// External reference ID (e.g., PayPal payment ID)
        /// </summary>
        public string? ReferenceId { get; set; }

        /// <summary>
        /// When this transaction was created
        /// </summary>
        public required DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Transaction type constants
    /// </summary>
    public static class CreditTransactionType
    {
        public const string Initial = "initial";
        public const string Consume = "consume";
        public const string TopUp = "topup";
        public const string Refund = "refund";
        public const string Bonus = "bonus";

        /// <summary>Monthly subscription quota reset (balance was SET to the quota, not added).
        /// Distinct from TopUp, which was additive.</summary>
        public const string SubscriptionGrant = "subscription_grant";
    }
}
