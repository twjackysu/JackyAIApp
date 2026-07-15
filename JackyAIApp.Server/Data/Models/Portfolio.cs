using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using JackyAIApp.Server.Data.Models.SQL;

namespace JackyAIApp.Server.Data.Models.Portfolio
{
    /// <summary>
    /// Append-only — values are persisted as int, reordering would corrupt existing rows.
    /// Adding a new type? Follow the checklist in PORTFOLIO_LIABILITY_PLAN.md section 6.
    /// </summary>
    public enum AssetType { STOCK, CASH, FUND, LIABILITY }

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

        /// <summary>Pricing/NAV currency, "TWD" | "USD". Drives market-value conversion.</summary>
        public required string Currency { get; set; }

        /// <summary>
        /// Currency the cost basis was paid in, "TWD" | "USD". May differ from <see cref="Currency"/>
        /// (e.g. a USD-priced fund bought with TWD). Null falls back to <see cref="Currency"/>.
        /// </summary>
        public string? CostCurrency { get; set; }

        /// <summary>
        /// Shares (STOCK) / units (FUND) / amount (CASH) / amount owed (LIABILITY).
        /// Always stored positive, even for LIABILITY — the sign flip to a negative
        /// market value happens exactly once, in PortfolioService's valuation loop.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>Manual NAV for funds. Ignored for stocks (live price), 1 for cash/liability.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>
        /// Optional total cost basis in position currency. Drives unrealized P&amp;L when present.
        /// Always null for LIABILITY — debt has no cost basis and must never enter P&amp;L/XIRR.
        /// </summary>
        public decimal? CostBasis { get; set; }
    }

    /// <summary>
    /// Per-user Portfolio preferences. A standing setting, not a point-in-time fact like
    /// <see cref="HoldingSnapshot.UsdTwdRate"/>, so it lives in its own single-row-per-user
    /// table rather than being duplicated into every snapshot.
    /// </summary>
    public class PortfolioSettings
    {
        [Key]
        public required string UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>
        /// TW brokerage commission discount multiplier (e.g. 0.28 = 2.8折). Applied to the
        /// standard 0.1425% rate when estimating a TW stock/ETF position's sell-side
        /// transaction cost in PortfolioService.
        /// </summary>
        public decimal FeeDiscountRate { get; set; } = 0.28m;
    }
}
