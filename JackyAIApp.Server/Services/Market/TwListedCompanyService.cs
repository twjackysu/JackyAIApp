using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.ExternalApi.Twse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using TWStockLib.Models;

namespace JackyAIApp.Server.Services.Market
{
    /// <summary>
    /// The TW symbol list (上市 and 上櫃 stocks, ETFs, ETNs, preferred shares and DRs, without
    /// warrants): names for the holdings editor's stock search, and which exchange each symbol
    /// trades on for TW price fetching.
    /// </summary>
    public interface ITwListedCompanyService
    {
        /// <summary>Symbol-prefix or name matches, symbol-prefix matches first. Reads the stored
        /// list only — never waits on an upstream fetch.</summary>
        Task<List<StockSearchItem>> SearchAsync(string keyword, CancellationToken ct = default);

        /// <summary>TSE (上市) or OTC (上櫃); null when the symbol isn't in the list (興櫃, a listing
        /// newer than the last refresh, a typo). Reads the stored list only.</summary>
        Task<MarketType?> GetMarketTypeAsync(string symbol, CancellationToken ct = default);

        /// <summary>Fetches both exchanges' lists and merges them into the stored list. Returns
        /// false, having written nothing, when either exchange's list could not be fetched.</summary>
        Task<bool> RefreshAsync(CancellationToken ct = default);

        /// <summary>When a refresh last succeeded; null when the list has never been filled.</summary>
        Task<DateTime?> GetLastRefreshedAtAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Fills <see cref="TwListedCompany"/> from TWSE's and TPEx's OpenAPI daily-quote dumps, and
    /// serves search and market lookups from an in-memory copy of it.
    ///
    /// These replaced TWStockLib's <c>GetAllStockListAsync</c>, which scrapes isin.twse.com.tw:
    /// 12MB of HTML (the 上市 page alone measured 9MB / 76s) against a 30s client timeout, so it
    /// failed every time. Its refresh also ran inside the search request and a failure left the
    /// list marked stale, so every search retried it and every search took 30s. The two OpenAPI
    /// dumps are ~5MB of JSON in ~5s, and refreshing is now
    /// <see cref="TwListedCompanyRefreshService"/>'s job, off the request path.
    ///
    /// The same scrape also answered "上市 or 上櫃?" for TW price fetching, before every realtime
    /// quote from 09:00 on, so it timed out on every TW price too. Which dump a symbol came from
    /// answers that, so <see cref="GetMarketTypeAsync"/> replaced it there as well.
    /// </summary>
    public class TwListedCompanyService(
        IDbContextFactory<AzureSQLDBContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<TwListedCompanyService> logger,
        ITwseOpenApi twse,
        TimeProvider? timeProvider = null) : ITwListedCompanyService
    {
        // The 上市 list is TWSE's daily quotes (STOCK_DAY_ALL, through ITwseOpenApi): every 上市
        // security that traded on the latest trading day, stocks, ETFs, ETNs, preferred shares, DRs.

        // The 上櫃 equivalent: stocks and ETFs, ~1,000 rows, 357KB (58KB gzipped). Not
        // tpex_mainboard_daily_close_quotes, which adds ~11,000 warrants for 4.7MB: from the East US
        // App Service TPEx sends a few KB a second, and cuts the response off after ~25s, so
        // that one never arrived whole there.
        internal const string TpexUrl = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes";

        /// <summary>Named client for the TPEx dump: gzip, and a timeout sized for a slow
        /// Taiwan-to-US path rather than for a user waiting on a request (see
        /// PortfolioServiceExtensions). The TWSE dump comes through <see cref="ITwseOpenApi"/>'s own.</summary>
        internal const string HttpClientName = "TwListedCompanies";

        private const string CacheKey = "tw_listed_companies";
        private const int MaxResults = 15;

        /// <summary>
        /// Both dumps list only what traded on the latest trading day, so a suspended stock drops
        /// out of them for as long as it is suspended. A row is therefore removed only after this
        /// long unseen — long enough to ride out a suspension, short enough that delisted symbols
        /// don't linger in search.
        /// </summary>
        internal static readonly TimeSpan PruneAfterUnseen = TimeSpan.FromDays(30);

        private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

        public async Task<List<StockSearchItem>> SearchAsync(string keyword, CancellationToken ct = default)
        {
            keyword = (keyword ?? "").Trim();
            if (keyword.Length == 0) return [];

            var companies = (await GetAllAsync(ct)).Entries;
            return companies
                .Where(c => c.Symbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
                    || c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Symbol.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c.Symbol, StringComparer.Ordinal)
                .Take(MaxResults)
                .Select(c => new StockSearchItem { Symbol = c.Symbol, Name = c.Name, Market = "TW" })
                .ToList();
        }

        public async Task<MarketType?> GetMarketTypeAsync(string symbol, CancellationToken ct = default)
        {
            var companies = await GetAllAsync(ct);
            return companies.BySymbol.TryGetValue((symbol ?? "").Trim(), out var entry)
                && Enum.TryParse<MarketType>(entry.Market, out var market)
                    ? market
                    : null;
        }

        /// <summary>The whole list, ~2,500 rows, held in memory so a keystroke or a price fetch
        /// costs a lookup in that rather than a database round trip. The hour's expiry only
        /// matters for changes made outside this process (another instance's refresh, a manual
        /// edit); this process's own refresh evicts it at once.</summary>
        private Task<CompanyList> GetAllAsync(CancellationToken ct) =>
            // An empty list isn't cached: before the first refresh has finished, the next lookup
            // should look again instead of finding nothing for an hour.
            cache.GetOrCreateSingleFlightAsync(CacheKey, TimeSpan.FromHours(1), async () =>
            {
                // Shared by every caller waiting on it, so no one caller's token may stop it.
                await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
                var entries = await db.TwListedCompanies
                    .AsNoTracking()
                    .Select(c => new Entry(c.Symbol, c.Name, c.Market))
                    .ToListAsync(CancellationToken.None);
                return new CompanyList(entries, entries.ToDictionary(e => e.Symbol, StringComparer.OrdinalIgnoreCase));
            }, ct, shouldCache: list => list.BySymbol.Count > 0);

        public async Task<DateTime?> GetLastRefreshedAtAsync(CancellationToken ct = default)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.TwListedCompanies.MaxAsync(c => (DateTime?)c.LastRefreshedAt, ct);
        }

        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            var twseTask = FetchTwseListAsync(ct);
            var tpexTask = FetchListAsync(TpexUrl, "SecuritiesCompanyCode", "CompanyName", MarketType.OTC, ct);
            var twse = await twseTask;
            var tpex = await tpexTask;

            // All or nothing. With one exchange missing, a refresh would still be recorded as a
            // success, and day after day the missing exchange's symbols would age towards
            // PruneAfterUnseen while nothing flagged the refresh as broken.
            if (twse is null || tpex is null) return false;

            var now = _time.GetUtcNow().UtcDateTime;
            var fresh = new Dictionary<string, (string Name, string Market)>(StringComparer.Ordinal);
            foreach (var (symbol, name, market) in twse.Concat(tpex))
                fresh.TryAdd(symbol, (name, market));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.TwListedCompanies.ToDictionaryAsync(c => c.Symbol, StringComparer.Ordinal, ct);

            foreach (var (symbol, (name, market)) in fresh)
            {
                if (existing.TryGetValue(symbol, out var row))
                {
                    row.Name = name; // renames happen (e.g. after mergers), and the newest name is the one people search for
                    row.Market = market; // and so do moves from 上櫃 to 上市
                    row.LastRefreshedAt = now;
                }
                else
                {
                    db.TwListedCompanies.Add(new TwListedCompany { Symbol = symbol, Name = name, Market = market, LastRefreshedAt = now });
                }
            }

            var pruneBefore = now - PruneAfterUnseen;
            var pruned = existing.Values
                .Where(c => !fresh.ContainsKey(c.Symbol) && c.LastRefreshedAt < pruneBefore)
                .ToList();
            db.TwListedCompanies.RemoveRange(pruned);

            await db.SaveChangesAsync(ct);
            cache.Remove(CacheKey);

            logger.LogInformation(
                "TW listed-company list refreshed: {Twse} TWSE + {Tpex} TPEx symbols, {Added} added, {Pruned} pruned",
                twse.Count, tpex.Count, fresh.Keys.Count(s => !existing.ContainsKey(s)), pruned.Count);
            return true;
        }

        /// <summary>The 上市 (symbol, name, market) rows, warrants dropped. Null when TWSE's quotes could
        /// not be read (<see cref="ITwseOpenApi"/> logs why) or leave nothing to list.</summary>
        private async Task<List<(string Symbol, string Name, string Market)>?> FetchTwseListAsync(CancellationToken ct)
        {
            var quotes = await twse.GetDailyQuotesAsync(ct);
            if (quotes is null) return null;

            var list = quotes
                .Where(q => q.Name.Length > 0 && !TwStockCode.IsWarrant(q.Code))
                .Select(q => (q.Code, q.Name, MarketType.TSE.ToString()))
                .ToList();
            if (list.Count == 0)
            {
                logger.LogWarning("TWSE listed-company list came back empty");
                return null;
            }
            return list;
        }

        /// <summary>(symbol, name, market) rows from one exchange's dump, warrants dropped. Null on
        /// any failure, including an empty list — neither exchange has ever had nothing listed, so
        /// an empty array means a broken response, not an empty market.</summary>
        private async Task<List<(string Symbol, string Name, string Market)>?> FetchListAsync(
            string url, string symbolField, string nameField, MarketType market, CancellationToken ct)
        {
            var source = market == MarketType.TSE ? "TWSE" : "TPEx";
            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger.LogWarning("{Source} listed-company list is not a JSON array", source);
                    return null;
                }

                var list = new List<(string, string, string)>();
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    var symbol = ReadString(row, symbolField);
                    var name = ReadString(row, nameField);
                    if (symbol.Length == 0 || name.Length == 0 || TwStockCode.IsWarrant(symbol)) continue;
                    list.Add((symbol, name, market.ToString()));
                }

                if (list.Count == 0)
                {
                    logger.LogWarning("{Source} listed-company list came back empty", source);
                    return null;
                }
                return list;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "{Source} listed-company list fetch failed", source);
                return null;
            }
        }

        private static string ReadString(JsonElement row, string field) =>
            row.ValueKind == JsonValueKind.Object
            && row.TryGetProperty(field, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";

        private sealed record Entry(string Symbol, string Name, string? Market);

        private sealed record CompanyList(IReadOnlyList<Entry> Entries, IReadOnlyDictionary<string, Entry> BySymbol);
    }
}
