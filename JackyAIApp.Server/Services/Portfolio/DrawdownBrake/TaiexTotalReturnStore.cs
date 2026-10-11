using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Portfolio.DrawdownBrake
{
    /// <summary>The stored span of the index series.</summary>
    public readonly record struct TaiexStoredRange(DateOnly Earliest, DateOnly Latest, int Count);

    public interface ITaiexTotalReturnStore
    {
        /// <summary>Null when nothing is stored.</summary>
        Task<TaiexStoredRange?> GetRangeAsync(CancellationToken ct = default);

        /// <summary>The whole stored series, ascending by date.</summary>
        Task<IReadOnlyList<IndexClose>> GetAllAsync(CancellationToken ct = default);

        /// <summary>Inserts new days and overwrites changed ones; storing the same days again is a
        /// no-op. Returns how many rows were added or changed.</summary>
        Task<int> UpsertAsync(IReadOnlyCollection<IndexClose> closes, CancellationToken ct = default);
    }

    /// <summary>
    /// Reads and writes <see cref="TaiexTotalReturnDaily"/>. Each call opens its own short-lived
    /// context, so the background refresh and request-time reads never share one (a DbContext
    /// allows one operation at a time — see <see cref="FxRateStore"/>).
    /// </summary>
    public class TaiexTotalReturnStore(IDbContextFactory<AzureSQLDBContext> dbFactory) : ITaiexTotalReturnStore
    {
        public async Task<TaiexStoredRange?> GetRangeAsync(CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var count = await db.TaiexTotalReturnDailies.CountAsync(ct);
            if (count == 0) return null;

            var earliest = await db.TaiexTotalReturnDailies.MinAsync(t => t.TradeDate, ct);
            var latest = await db.TaiexTotalReturnDailies.MaxAsync(t => t.TradeDate, ct);
            return new TaiexStoredRange(earliest, latest, count);
        }

        public async Task<IReadOnlyList<IndexClose>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.TaiexTotalReturnDailies.AsNoTracking()
                .OrderBy(t => t.TradeDate)
                .Select(t => new IndexClose(t.TradeDate, t.Close))
                .ToListAsync(ct);
        }

        public async Task<int> UpsertAsync(IReadOnlyCollection<IndexClose> closes, CancellationToken ct = default)
        {
            if (closes.Count == 0) return 0;

            // One value per date, the last one winning, so a repeated date in the input cannot
            // turn into two inserts for the same key.
            var byDate = closes.GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.Last().Close);

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var dates = byDate.Keys.ToList();
            var existing = await db.TaiexTotalReturnDailies
                .Where(t => dates.Contains(t.TradeDate))
                .ToDictionaryAsync(t => t.TradeDate, ct);

            var changed = 0;
            foreach (var (date, close) in byDate)
            {
                if (existing.TryGetValue(date, out var row))
                {
                    if (row.Close == close) continue;
                    row.Close = close;
                }
                else
                {
                    db.TaiexTotalReturnDailies.Add(new TaiexTotalReturnDaily { TradeDate = date, Close = close });
                }
                changed++;
            }

            if (changed > 0) await db.SaveChangesAsync(ct);
            return changed;
        }
    }
}
