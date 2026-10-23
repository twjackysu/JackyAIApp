using System.ComponentModel.DataAnnotations.Schema;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// Per-user, per-UTC-day AI usage ledger backing the usage-limit gate that replaces credit
    /// consumption (DECISION-006/007). One row per (UserId, Date) — composite primary key, not a
    /// surrogate id, so <see cref="Services.IUsageLimitService"/> can target the exact row with a
    /// single atomic conditional UPDATE (or insert-if-missing) instead of a read-modify-write
    /// round trip. See ARCHITECTURE.md "Credits &amp; Payments 移除設計".
    /// </summary>
    public class DailyUsage
    {
        public required string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;

        /// <summary>UTC calendar date (time component always midnight).</summary>
        public required DateTime Date { get; set; }

        /// <summary>Weighted usage consumed so far today, in <see cref="Common.UsageCosts"/> units.</summary>
        public ulong Used { get; set; }
    }
}
