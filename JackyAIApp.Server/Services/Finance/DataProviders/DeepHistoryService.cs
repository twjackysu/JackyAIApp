using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <param name="Stocks">The input prices, with older stored bars added for the stocks that have them.</param>
    /// <param name="Backfill">How far the background fetching of what is still missing has got; null
    /// when none of the stocks asked about has anything to backfill.</param>
    public sealed record DeepHistoryResult(Dictionary<string, MarketData> Stocks, BackfillStatus? Backfill);

    /// <summary>
    /// Takes the ~6 months of prices the providers hand out and extends them backwards with what the
    /// background backfill (<see cref="ITwHistoryBackfill"/>) has stored, for charts that reach
    /// further back (the drawdown and benchmark charts, up to 3 years). It only ever reads the
    /// database: a month that has not been fetched yet is queued for the background, never waited
    /// for, so a request is as quick as it was with 6 months.
    /// </summary>
    public interface IDeepHistoryService
    {
        Task<DeepHistoryResult> ExtendAsync(
            IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyList<HistoryWant> wants,
            DateOnly targetFrom, CancellationToken ct);
    }

    public sealed class DeepHistoryService(
        ITwHistoryBackfill backfill,
        IDbContextFactory<AzureSQLDBContext> dbFactory,
        IMarketDataProviderFactory providers) : IDeepHistoryService
    {
        public async Task<DeepHistoryResult> ExtendAsync(
            IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyList<HistoryWant> wants,
            DateOnly targetFrom, CancellationToken ct)
        {
            var extended = new Dictionary<string, MarketData>(stocks);

            // Only TW stocks are stored by the backfill; a stock whose 6-month fetch failed has
            // nothing to extend.
            var tw = wants
                .Where(w => stocks.ContainsKey(w.Symbol) && providers.DetectRegion(w.Symbol) == MarketRegion.TW)
                .DistinctBy(w => w.Symbol)
                .ToList();
            if (tw.Count == 0) return new DeepHistoryResult(extended, null);

            var status = await backfill.RequestAsync(tw, targetFrom, ct);

            var symbols = tw.Select(w => w.Symbol).ToList();
            var oldest = tw.Min(w => TwHistoryBackfillService.MonthStart(w.NeedFrom));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = (await db.DailyStockPrices
                    .AsNoTracking()
                    .Where(p => p.Market == DailyStockPriceWriter.Market && symbols.Contains(p.Symbol) && p.TradeDate >= oldest)
                    .ToListAsync(ct))
                .ToLookup(p => p.Symbol);
            var marketTypes = await db.StockSymbolInfos
                .AsNoTracking()
                .Where(i => symbols.Contains(i.Symbol))
                .ToDictionaryAsync(i => i.Symbol, i => i.TwMarketType, ct);

            foreach (var want in tw)
            {
                var recent = stocks[want.Symbol];
                // Everything from the first bar the providers gave back is theirs (and includes
                // today's overlay); stored bars are only added in front of it.
                var firstRecent = recent.HistoricalPrices.Count > 0
                    ? DateOnly.FromDateTime(recent.HistoricalPrices.Min(p => p.Date))
                    : DateOnly.MaxValue;
                var from = TwHistoryBackfillService.MonthStart(want.NeedFrom);
                var factor = TwVolume.ToSharesFactor(marketTypes.GetValueOrDefault(want.Symbol));

                var older = rows[want.Symbol]
                    .Where(p => p.TradeDate >= from && p.TradeDate < firstRecent)
                    .OrderBy(p => p.TradeDate)
                    .Select(p => new DailyPrice
                    {
                        Date = p.TradeDate.ToDateTime(TimeOnly.MinValue),
                        Open = p.Open,
                        High = p.High,
                        Low = p.Low,
                        Close = p.Close,
                        Volume = p.Volume * factor,
                    })
                    .ToList();
                if (older.Count == 0) continue;

                // A copy: the input is the cached instance every other request shares.
                extended[want.Symbol] = new MarketData
                {
                    StockCode = recent.StockCode,
                    CompanyName = recent.CompanyName,
                    HistoricalPrices = [.. older, .. recent.HistoricalPrices],
                    Fundamentals = recent.Fundamentals,
                    Chips = recent.Chips,
                    FetchedAt = recent.FetchedAt,
                };
            }

            return new DeepHistoryResult(extended, status);
        }
    }
}
