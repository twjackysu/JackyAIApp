using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Orphaned since TASK-024 (DECISION-006/007) — the underlying <c>CreditTransactions</c>
    /// table is dropped by migration <c>20261001160000_DropCreditPaymentSchema</c>, and this
    /// type is no longer mapped in <c>AzureSQLDBContext</c> (no <c>DbSet</c>, no fluent config,
    /// no <c>User</c> navigation property). Nothing in the codebase constructs, reads, or writes
    /// it. Left in place only because this run's sandbox could not stage the file deletion
    /// itself (see TASK-024.md) — safe to delete outright in a follow-up commit.
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
