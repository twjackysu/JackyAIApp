using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Data.Models.Portfolio
{
    public enum TransactionType { BUY, SELL, DIVIDEND }

    public class PortfolioTransaction
    {
        [Key]
        public int Id { get; set; }

        public required string UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public required string Symbol { get; set; }
        public required string Market { get; set; }   // "TW" | "US"
        public required string CompanyName { get; set; }
        public required TransactionType Type { get; set; }
        public required DateTime Date { get; set; }
        public required decimal Quantity { get; set; }
        public required decimal Price { get; set; }
        public required string Currency { get; set; } // "TWD" | "USD"
        public decimal Fee { get; set; } = 0;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class NetWorthSnapshot
    {
        [Key]
        public int Id { get; set; }

        public required string UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public required DateOnly Date { get; set; }
        public required decimal TotalTWD { get; set; }
        public decimal TwStockTWD { get; set; }
        public decimal UsStockTWD { get; set; }
        public decimal UsdTwdRate { get; set; }
    }
}
