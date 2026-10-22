using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>Reads a user's holdings snapshots.</summary>
    public interface IHoldingSnapshotReader
    {
        /// <summary>The user's most recent snapshot, with its positions; null when there is none.</summary>
        Task<HoldingSnapshot?> LatestAsync(string userId, CancellationToken ct);

        /// <summary>
        /// The position the latest snapshot holds for <paramref name="market"/>/<paramref name="symbol"/>:
        /// a fund by its id when the market is FUND, otherwise a stock on that market. Null when it isn't held.
        /// </summary>
        Task<HoldingPosition?> FindLatestPositionAsync(string userId, string market, string symbol, CancellationToken ct);
    }

    public class HoldingSnapshotReader(AzureSQLDBContext db) : IHoldingSnapshotReader
    {
        public async Task<HoldingSnapshot?> LatestAsync(string userId, CancellationToken ct) =>
            await db.HoldingSnapshots
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EffectiveDate)
                .FirstOrDefaultAsync(ct);

        public async Task<HoldingPosition?> FindLatestPositionAsync(
            string userId, string market, string symbol, CancellationToken ct)
        {
            var snapshot = await LatestAsync(userId, ct);
            var sym = symbol.ToUpperInvariant();
            var upperMarket = market.ToUpperInvariant();
            return snapshot?.Positions.FirstOrDefault(p => upperMarket == "FUND"
                ? p.AssetType == AssetType.FUND && p.Symbol == sym
                : p.AssetType == AssetType.STOCK && p.Market == upperMarket && p.Symbol == sym);
        }
    }
}
