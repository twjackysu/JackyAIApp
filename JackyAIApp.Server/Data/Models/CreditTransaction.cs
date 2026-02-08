using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Records all credit transactions (consumption, top-up, refund, bonus)
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
        /// External reference ID (e.g., Stripe payment ID)
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
    }
}
