using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using Microsoft.EntityFrameworkCore;
using TWStockLib.Models;

namespace JackyAIApp.Server.Services.Finance.DataProviders
{
    /// <summary>
    /// DB-backed decorator around <see cref="TWStockLibHistoricalProvider"/>: finalized daily bars
    /// are immutable and shared across all users, so once a (symbol, date) is persisted it is never
    /// refetched — only the gap between the DB's latest bar and the last completed trading day is
    /// pulled from TWStockLib.
    ///
    /// This exists because the app runs on Azure App Service's free tier, which cold-starts after
    /// ~20 min idle and wipes any <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>
    /// entirely. For a personal-use app opened a few times a day, nearly every load is cold, so
    /// in-memory caching (PR #207) barely helps — but the Azure SQL Basic tier is DTU-provisioned
    /// and does NOT auto-pause on idle (only Serverless vCore tier does), so it stays warm across
    /// web cold starts. Persisting here is what actually survives a cold start; see
    /// .claude/plans/portfolio-db-price-cache.md for the full writeup.
    ///
    /// Phase 1 scope: TW stocks only (isin/STOCK_DAY are the dominant cost — see the plan doc).
    ///
    /// Uses <see cref="IDbContextFactory{TContext}"/> rather than an injected scoped DbContext:
    /// PortfolioService fans this out across many symbols via Task.WhenAll within one request, and
    /// a shared scoped DbContext isn't thread-safe for concurrent use — that combination briefly
    /// shipped and caused "A second operation was started on this context instance..." exceptions
    /// in prod, surfacing as spurious "報價失敗" on whichever symbols lost the race. Each FetchAsync
    /// call now creates its own short-lived context, so concurrent symbol fetches never share one.
    /// </summary>
    public class PersistentTwHistoricalProvider : IMarketDataProvider
    {
        private readonly ITWStockLibHistoricalProvider _inner;
        private readonly IDbContextFactory<AzureSQLDBContext> _dbFactory;
        private readonly ILogger<PersistentTwHistoricalProvider> _logger;

        private const string Market = "TW";
        private const int LookbackMonths = 6;

        public DataProviderType Type => DataProviderType.HistoricalPrice;

        public PersistentTwHistoricalProvider(
            ITWStockLibHistoricalProvider inner,
            IDbContextFactory<AzureSQLDBContext> dbFactory,
            ILogger<PersistentTwHistoricalProvider> logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<MarketData> FetchAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var windowStart = today.AddMonths(-LookbackMonths);

            var existing = await db.DailyStockPrices
                .Where(p => p.Symbol == stockCode && p.Market == Market && p.TradeDate >= windowStart)
                .OrderBy(p => p.TradeDate)
                .ToListAsync(cancellationToken);

            var maxDate = existing.Count > 0 ? existing[^1].TradeDate : (DateOnly?)null;
            var lastTradingDay = LastTradingDayBefore(today);
            // Once the session has closed, today's own bar is the latest finalized trading
            // day — without this, needsFetch never fires for "today" and the DB (and the
            // response built from it) stays pinned to yesterday's close until midnight rolls
            // the calendar date over, even though TWSE published today's close hours ago.
            var todayIsFinalized = IsWeekday(today) && IsPastTwCloseTime();
            var latestAvailableTradingDay = todayIsFinalized ? today : lastTradingDay;
            var needsFetch = maxDate == null || maxDate.Value < latestAvailableTradingDay;

            var companyName = await GetPersistedCompanyNameAsync(db, stockCode, cancellationToken);

            if (needsFetch)
            {
                try
                {
                    var marketType = await ResolveAndPersistMarketTypeAsync(db, stockCode, cancellationToken);
                    // Only ask TWStockLib for what's actually missing — the whole point of this
                    // provider is to stop paying the full 6-month cost on every cold start.
                    var fetchStart = maxDate?.AddDays(1).ToDateTime(TimeOnly.MinValue)
                        ?? windowStart.ToDateTime(TimeOnly.MinValue);
                    var fetched = await _inner.FetchRangeAsync(
                        stockCode, fetchStart, DateTime.Now, marketType, cancellationToken);

                    if (!string.IsNullOrWhiteSpace(fetched.CompanyName))
                    {
                        companyName = fetched.CompanyName;
                        await PersistCompanyNameAsync(db, stockCode, fetched.CompanyName, cancellationToken);
                    }

                    await UpsertFinalizedBarsAsync(db, stockCode, fetched.HistoricalPrices, today, cancellationToken);

                    // Re-read so the returned list is the consistent, sorted merge of old + new.
                    existing = await db.DailyStockPrices
                        .Where(p => p.Symbol == stockCode && p.Market == Market && p.TradeDate >= windowStart)
                        .OrderBy(p => p.TradeDate)
                        .ToListAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    // Fall through and serve whatever's already in the DB rather than failing the
                    // whole request — matches TWStockLibHistoricalProvider's own fail-soft behavior.
                    _logger.LogWarning(ex, "Gap-fill fetch failed for {Symbol}, serving DB-only data", stockCode);
                }
            }

            return new MarketData
            {
                StockCode = stockCode,
                CompanyName = companyName ?? "",
                HistoricalPrices = existing.Select(p => new DailyPrice
                {
                    Date = p.TradeDate.ToDateTime(TimeOnly.MinValue),
                    Open = p.Open,
                    High = p.High,
                    Low = p.Low,
                    Close = p.Close,
                    Volume = p.Volume,
                }).ToList(),
            };
        }

        /// <summary>Most recent weekday before <paramref name="today"/>. Doesn't model exchange
        /// holidays (same tolerance as PortfolioService.IsTwTradingHours) — worst case is one
        /// harmless extra gap-fetch that returns no new rows.</summary>
        private static DateOnly LastTradingDayBefore(DateOnly today)
        {
            var d = today.AddDays(-1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                d = d.AddDays(-1);
            return d;
        }

        private static bool IsWeekday(DateOnly date) =>
            date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        /// <summary>TSE/TPEx regular session close (13:30 Taipei). Taiwan has no DST, so a fixed
        /// UTC+8 offset is exact — matches PortfolioService.IsTwTradingHours' same approach.</summary>
        private static bool IsPastTwCloseTime()
        {
            var taipeiNow = DateTime.UtcNow.AddHours(8);
            return taipeiNow.TimeOfDay >= new TimeSpan(13, 30, 0);
        }

        private static async Task<string?> GetPersistedCompanyNameAsync(
            AzureSQLDBContext db, string symbol, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            return info?.Name;
        }

        private async Task<MarketType> ResolveAndPersistMarketTypeAsync(
            AzureSQLDBContext db, string symbol, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info != null && Enum.TryParse<MarketType>(info.TwMarketType, out var cached))
                return cached;

            // Falls through to TWStockLibHistoricalProvider's own memory-cached isin lookup —
            // still only hits isin.twse.com.tw once per market per 24h (PR #207) even for
            // symbols not yet persisted here; this DB layer just means a *previously seen*
            // symbol survives a cold start without needing isin at all.
            var resolved = await _inner.DetermineMarketType(symbol);

            if (info == null)
            {
                db.StockSymbolInfos.Add(new StockSymbolInfo
                {
                    Symbol = symbol,
                    Market = Market,
                    TwMarketType = resolved.ToString(),
                    LastResolvedAt = DateTime.UtcNow,
                });
            }
            else
            {
                info.TwMarketType = resolved.ToString();
                info.LastResolvedAt = DateTime.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Two concurrent cold-start requests resolving the same never-seen symbol can
                // race; the loser's insert fails the unique PK, which is fine — the winner's row
                // is what matters, we just return our own in-memory `resolved` either way.
                _logger.LogDebug(ex, "Race persisting market type for {Symbol}, ignoring", symbol);
            }

            return resolved;
        }

        private async Task PersistCompanyNameAsync(
            AzureSQLDBContext db, string symbol, string name, CancellationToken ct)
        {
            var info = await db.StockSymbolInfos.FindAsync([symbol], ct);
            if (info == null || info.Name == name) return;

            info.Name = name;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogDebug(ex, "Race persisting company name for {Symbol}, ignoring", symbol);
            }
        }

        /// <summary>Persists bars strictly before <paramref name="today"/> unconditionally, plus
        /// today's own bar once the session has closed (<see cref="IsPastTwCloseTime"/>) — before
        /// that, today's price is still moving intraday and must never be frozen into the DB as a
        /// "close".</summary>
        private async Task UpsertFinalizedBarsAsync(
            AzureSQLDBContext db, string symbol, List<DailyPrice> bars, DateOnly today, CancellationToken ct)
        {
            var todayIsFinalized = IsWeekday(today) && IsPastTwCloseTime();
            var finalized = bars
                .Where(b =>
                {
                    var d = DateOnly.FromDateTime(b.Date);
                    return (d < today || (d == today && todayIsFinalized)) && b.Close > 0;
                })
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
                // Concurrent cold-start requests can race to insert the same (Symbol, TradeDate) —
                // the unique index rejects the loser, which is fine, the winner's row is identical
                // data anyway (finalized bars are immutable).
                _logger.LogDebug(ex, "Concurrent insert race for {Symbol} price rows, ignoring", symbol);
            }
        }
    }
}
