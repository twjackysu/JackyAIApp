using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.Portfolio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Reads a user's holdings snapshots, each read on a context of its own (so a read one request
    /// starts and others join never depends on that request's DbContext). Writes stay with the
    /// request's own context in <see cref="PortfolioService"/>, which calls <see cref="Invalidate"/>
    /// after each one.
    /// </summary>
    public interface IHoldingSnapshotReader
    {
        /// <summary>The user's most recent snapshot, with its positions; null when there is none.</summary>
        Task<HoldingSnapshot?> LatestAsync(string userId, CancellationToken ct);

        /// <summary>
        /// The position the latest snapshot holds for <paramref name="market"/>/<paramref name="symbol"/>:
        /// a fund by its id when the market is FUND, otherwise a stock on that market. Null when it isn't held.
        /// </summary>
        Task<HoldingPosition?> FindLatestPositionAsync(string userId, string market, string symbol, CancellationToken ct);

        /// <summary>
        /// Every snapshot for a user, oldest first — the full timeline, not just the latest.
        ///
        /// A single page load (summary + net-worth-history ×2 + benchmark + drawdown) asks for this
        /// 5 times for the same user. Short-TTL cache + single-flight collapses that into one
        /// Include(Positions) query per burst; the TTL is short enough that a stale read between
        /// edits is not a real concern, and every write calls <see cref="Invalidate"/> anyway.
        /// The snapshots are shared from the cache: read them, never change them.
        /// </summary>
        Task<List<HoldingSnapshot>> AllAsync(string userId, CancellationToken ct);

        /// <summary>Drops the user's cached timeline; call after saving or deleting a snapshot.</summary>
        void Invalidate(string userId);
    }

    public class HoldingSnapshotReader(IDbContextFactory<AzureSQLDBContext> dbFactory, IMemoryCache cache) : IHoldingSnapshotReader
    {
        private const string SNAPSHOTS_CACHE_PREFIX = "portfolio_snapshots_";

        // Deliberately shorter than every other Portfolio TTL (CacheTtl.cs) — this only needs to
        // survive one page-load burst of concurrent requests, not actually keep data "fresh" across
        // user edits; every save/delete invalidates it immediately regardless.
        private static readonly TimeSpan SnapshotsCacheTtl = TimeSpan.FromSeconds(30);

        public async Task<HoldingSnapshot?> LatestAsync(string userId, CancellationToken ct)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.HoldingSnapshots
                .AsNoTracking()
                .Include(s => s.Positions)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EffectiveDate)
                .FirstOrDefaultAsync(ct);
        }

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

        public Task<List<HoldingSnapshot>> AllAsync(string userId, CancellationToken ct) =>
            cache.GetOrCreateSingleFlightAsync(SNAPSHOTS_CACHE_PREFIX + userId, SnapshotsCacheTtl, async () =>
            {
                // Its own context: the read is shared by every request of the burst, and the one
                // that started it may end (its client gone) while the others still wait on it.
                await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                // No tracking: read-only, and shared from the cache across requests. Tracking
                // every position of every snapshot only added change-tracker work that grows
                // with the snapshot count.
                return await db.HoldingSnapshots
                    .AsNoTracking()
                    .Include(s => s.Positions)
                    .Where(s => s.UserId == userId)
                    .OrderBy(s => s.EffectiveDate)
                    .ToListAsync(CancellationToken.None);
            }, ct);

        public void Invalidate(string userId) => cache.Remove(SNAPSHOTS_CACHE_PREFIX + userId);
    }
}
