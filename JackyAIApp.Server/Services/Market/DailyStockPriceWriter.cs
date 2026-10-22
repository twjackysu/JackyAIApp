using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// The one way TW daily bars get into <see cref="DailyStockPrice"/>, shared by the on-demand
    /// gap-fill (<see cref="PersistentTwHistoricalProvider"/>) and the background backfill
    /// (<see cref="TwHistoryBackfillService"/>) so both apply the same rules.
    /// </summary>
    internal static class DailyStockPriceWriter
    {
        public const string Market = "TW";

        /// <summary>Persists bars strictly before <paramref name="today"/> — today's price is still
        /// moving intraday (or, even after close, TWSE/TWStockLib give no documented guarantee of
        /// exactly when it's actually final) and must never be frozen into the DB as a "close".
        /// Days already stored are left alone.</summary>
        public static async Task UpsertFinalizedAsync(
            AzureSQLDBContext db, string symbol, IReadOnlyCollection<DailyPrice> bars, DateOnly today,
            ILogger logger, CancellationToken ct)
        {
            var finalized = bars
                .Where(b => DateOnly.FromDateTime(b.Date) < today && b.Close > 0)
                .ToList();
            if (finalized.Count == 0) return;

            var dates = finalized.Select(b => DateOnly.FromDateTime(b.Date)).ToList();
            var existingDates = (await db.DailyStockPrices
                .Where(p => p.Symbol == symbol && p.Market == Market && dates.Contains(p.TradeDate))
                .Select(p => p.TradeDate)
                .ToListAsync(ct))
                .ToHashSet();

            var toAdd = finalized
                .Where(b => !existingDates.Contains(DateOnly.FromDateTime(b.Date)))
                .Select(b => new DailyStockPrice
                {
                    Symbol = symbol,
                    Market = Market,
                    TradeDate = DateOnly.FromDateTime(b.Date),
                    Open = b.Open,
                    High = b.High,
                    Low = b.Low,
                    Close = b.Close,
                    Volume = b.Volume,
                })
                .ToList();
            if (toAdd.Count == 0) return;

            db.DailyStockPrices.AddRange(toAdd);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Concurrent requests can race to insert the same (Symbol, TradeDate) — the unique
                // index rejects the loser, which is fine, the winner's row is identical data anyway
                // (finalized bars are immutable).
                logger.LogDebug(ex, "Concurrent insert race for {Symbol} price rows, ignoring", symbol);
            }
        }
    }
}
