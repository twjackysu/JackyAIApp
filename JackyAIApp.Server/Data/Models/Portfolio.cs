using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Data.Models.Portfolio
{
    public enum AssetType { STOCK, CASH, FUND }

    /// <summary>
    /// One full snapshot of a user's holdings as of an effective date.
    /// History is reconstructed by carrying forward the most recent snapshot
    /// whose EffectiveDate is on or before a given day.
    /// </summary>
    public class HoldingSnapshot
    {
        [Key]
        public int Id { get; set; }

        public required string UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        public required DateOnly EffectiveDate { get; set; }

        /// <summary>USD/TWD rate captured at save time; used for all valuation while this snapshot is active.</summary>
        public decimal UsdTwdRate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<HoldingPosition> Positions { get; set; } = [];
    }

    public class HoldingPosition
    {
        [Key]
        public int Id { get; set; }

        public int SnapshotId { get; set; }
        [ForeignKey(nameof(SnapshotId))]
        public HoldingSnapshot Snapshot { get; set; } = null!;

        public required AssetType AssetType { get; set; }

        /// <summary>Stock/fund code. Empty for cash.</summary>
        public string Symbol { get; set; } = "";

        /// <summary>Display name (company / fund / "現金").</summary>
        public string Name { get; set; } = "";

        /// <summary>"TW" | "US" for stocks; empty otherwise.</summary>
        public string Market { get; set; } = "";

        /// <summary>"TWD" | "USD".</summary>
        public required string Currency { get; set; }

        /// <summary>Shares (STOCK) / units (FUND) / amount (CASH).</summary>
        public decimal Quantity { get; set; }

        /// <summary>Manual NAV for funds. Ignored for stocks (live price), 1 for cash.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Optional total cost basis in position currency. Drives unrealized P&amp;L when present.</summary>
        public decimal? CostBasis { get; set; }
    }
}
