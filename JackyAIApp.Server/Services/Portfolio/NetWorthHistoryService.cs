using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using static JackyAIApp.Server.Services.Portfolio.PortfolioValuation;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>Net worth over time, by asset type, category or sub-category.</summary>
    public interface INetWorthHistoryService
    {
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default);
        Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, DateOnly? from, DateOnly? to, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType,
            CancellationToken ct = default);
    }

    public class NetWorthHistoryService(
        IHoldingSnapshotReader snapshotReader,
        IPortfolioPriceLoader priceLoader,
        IStockCategoryService categoryService,
        TimeProvider? timeProvider = null) : INetWorthHistoryService
    {
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // ── Net worth history ────────────────────────────────────────────────

        public Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, int days, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);
            var today = Today;
            return GetNetWorthHistoryCoreAsync(userId, today.AddDays(-days), today, groupBy, ct);
        }

        /// <summary>
        /// Net worth history over an explicit date window, for callers that let the user pick the
        /// start and end rather than a preset span.
        ///
        /// Lenient by design — every input resolves to some sensible window rather than an error,
        /// because these come straight from date inputs the user is editing:
        /// <list type="bullet">
        ///   <item>no <paramref name="to"/>, or one in the future → ends today</item>
        ///   <item>no <paramref name="from"/> → one calendar month before the end</item>
        ///   <item>from after to → swapped</item>
        ///   <item>from before the user's first snapshot → starts at the first snapshot</item>
        ///   <item>longer than one year → the most recent year of it</item>
        /// </list>
        /// </summary>
        public Task<List<NetWorthDataPoint>> GetNetWorthHistoryAsync(
            string userId, DateOnly? from, DateOnly? to, NetWorthGroupBy groupBy = NetWorthGroupBy.AssetType,
            CancellationToken ct = default)
        {
            var today = Today;
            if (from is { } f && to is { } t && f > t) (from, to) = (t, f);

            var end = to is { } requestedEnd && requestedEnd < today ? requestedEnd : today;
            var start = from ?? end.AddMonths(-1);
            if (start > end) start = end; // a start in the future: show the one day there is
            // At most one calendar year. The chart's own inputs already keep within it; this is the
            // limit for any other caller, so an arbitrary from/to can't make one request walk years
            // of daily valuations.
            var earliest = end.AddYears(-1);
            if (start < earliest) start = earliest;

            return GetNetWorthHistoryCoreAsync(userId, start, end, groupBy, ct);
        }

        private async Task<List<NetWorthDataPoint>> GetNetWorthHistoryCoreAsync(
            string userId, DateOnly startDate, DateOnly endDate, NetWorthGroupBy groupBy, CancellationToken ct)
        {
            var snapshots = await snapshotReader.AllAsync(userId, ct);

            if (snapshots.Count == 0) return [];

            // Nothing exists before the first snapshot, so a window reaching further back starts there.
            var firstDate = snapshots[0].EffectiveDate;
            if (startDate < firstDate) startDate = firstDate;

            // Fetch price history for every distinct stock symbol that appears in any snapshot
            var stockSymbols = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.STOCK)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            // Sorted ascending by date (defensively re-sorted here even though upstream fetchers
            // already do this, since the cursor walk below depends on it) so each symbol's "price
            // as of date" lookup can advance with a forward-only cursor instead of re-filtering +
            // re-sorting the whole history on every single day of the loop below.
            // Deep fetch — the day loop below walks the whole requested window per symbol.
            var priceMap = (await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct))
                .ToDictionary(kv => kv.Key, kv => kv.Value.HistoricalPrices.OrderBy(x => x.Date).ToList());
            var stockCursors = priceMap.Keys.ToDictionary(k => k, _ => 0);

            // Fund NAV histories — gives funds a real daily curve instead of a flat line.
            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            // FetchFundsAsync's results are cached (FundDataService, up to 6h) and shared with other
            // callers — build separate sorted copies here rather than reordering navHist.Prices
            // in place, which would mutate the shared cached instance.
            var fundNavMap = await priceLoader.FetchFundsAsync(fundIds, ct);
            var fundPriceMap = fundNavMap.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Prices.OrderBy(x => x.Date, StringComparer.Ordinal).ToList());
            var fundCursors = fundNavMap.Keys.ToDictionary(k => k, _ => 0);

            // Only resolved when actually needed (groupBy: category/subCategory) — one AI-
            // classification call per distinct STOCK/FUND symbol, warmed concurrently up front just
            // like the price fetches above rather than sequentially inside the day loop. CASH/
            // LIABILITY don't need a lookup (mapped directly by the switch below); most calls are
            // cache hits after the first time a symbol is ever seen (StockCategoryService persists
            // to StockSymbolInfo).
            var categoryMap = new Dictionary<string, PortfolioCategory>();
            var subCategoryMap = new Dictionary<string, string>();
            if (groupBy is NetWorthGroupBy.Category or NetWorthGroupBy.SubCategory)
            {
                var classifiable = snapshots
                    .SelectMany(s => s.Positions)
                    .Where(p => p.AssetType is AssetType.STOCK or AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                    .GroupBy(p => p.Symbol)
                    .Select(g => g.Last()) // most recent name/market info for that symbol
                    .ToList();
                var resolved = await Task.WhenAll(classifiable.Select(async p =>
                {
                    var (category, subCategory, _) = await categoryService.GetCategoryAsync(p.AssetType, p.Symbol, p.Name, p.Market, ct);
                    return (p.Symbol, Category: category, SubCategory: subCategory);
                }));
                categoryMap = resolved.ToDictionary(r => r.Symbol, r => r.Category);
                // Falls back to the (Traditional Chinese) category name when the AI didn't return a
                // subCategory (e.g. classification failed, or genuinely Other) — better than an
                // empty/undefined series key.
                subCategoryMap = resolved.ToDictionary(
                    r => r.Symbol, r => string.IsNullOrWhiteSpace(r.SubCategory) ? "未分類" : r.SubCategory);
            }

            // Snapshots are sorted ascending by EffectiveDate (IHoldingSnapshotReader.AllAsync), and `date` only
            // ever increases below — walk a single cursor forward instead of re-scanning the whole
            // snapshot list ("active = latest with EffectiveDate <= date") on every day.
            var snapshotIdx = 0;

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                var fx = SnapshotFx(active);
                var groups = new Dictionary<string, decimal>();
                decimal total = 0;
                var dt = date.ToDateTime(TimeOnly.MinValue);
                var dayKey = date.ToString("yyyy-MM-dd");

                void Add(string key, decimal valueTWD)
                {
                    groups[key] = groups.GetValueOrDefault(key) + valueTWD;
                    total += valueTWD;
                }

                foreach (var p in active.Positions)
                {
                    var fxMul = p.Currency == "USD" ? fx : 1m;

                    if (p.AssetType == AssetType.CASH)
                    {
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Cash), p.Quantity * fxMul);
                    }
                    else if (p.AssetType == AssetType.REAL_ESTATE)
                    {
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Property), p.Quantity * fxMul);
                    }
                    else if (p.AssetType == AssetType.LIABILITY)
                    {
                        // Negative — the sole sign-flip point, same invariant as the summary
                        // endpoint's valuation loop. Every downstream sum (here: `total`) just adds.
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Liability), -(p.Quantity * fxMul));
                    }
                    else if (p.AssetType == AssetType.FUND)
                    {
                        decimal nav = p.UnitPrice ?? 0; // fallback: flat value stored at save time
                        var fundFxMul = fxMul; // default to stored currency until NAV tells us otherwise
                        if (!string.IsNullOrWhiteSpace(p.Symbol) &&
                            fundNavMap.TryGetValue(p.Symbol, out var navHist))
                        {
                            fundFxMul = navHist.Currency == "USD" ? fx : 1m;
                            var prices = fundPriceMap[p.Symbol];
                            if (prices.Count > 0)
                            {
                                var idx = fundCursors[p.Symbol];
                                // Both sides are ISO yyyy-MM-dd strings → ordinal compare is chronological.
                                while (idx + 1 < prices.Count &&
                                       string.CompareOrdinal(prices[idx + 1].Date, dayKey) <= 0)
                                    idx++;
                                fundCursors[p.Symbol] = idx;
                                if (string.CompareOrdinal(prices[idx].Date, dayKey) <= 0 && prices[idx].Close > 0)
                                    nav = prices[idx].Close;
                            }
                        }
                        if (nav > 0)
                            Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Other), nav * p.Quantity * fundFxMul);
                    }
                    else // STOCK
                    {
                        if (!priceMap.TryGetValue(p.Symbol, out var prices) || prices.Count == 0) continue;
                        var idx = stockCursors[p.Symbol];
                        while (idx + 1 < prices.Count && prices[idx + 1].Date.Date <= dt)
                            idx++;
                        stockCursors[p.Symbol] = idx;
                        if (prices[idx].Date.Date > dt) continue; // no price at/before this date yet
                        var close = prices[idx].Close;
                        if (close <= 0) continue;
                        Add(GroupKey(groupBy, p, categoryMap, subCategoryMap, PortfolioCategory.Other), close * p.Quantity * fxMul);
                    }
                }

                // Only "== 0" (not "<= 0"): a day where nothing could be priced lands on exactly
                // 0 and should be skipped as no-data. A real negative total (debt exceeding assets)
                // is a valid data point and must not be dropped from the chart.
                if (total == 0) continue;

                result.Add(new NetWorthDataPoint
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    TotalTWD = total,
                    Groups = groups.Select(kv => new NetWorthGroupValue { Key = kv.Key, ValueTWD = kv.Value }).ToList(),
                });
            }

            return result;
        }

        /// <summary>Group key for one position under the requested groupBy mode. `fallbackCategory`
        /// is only used in Category mode, for asset types that skip the categoryMap lookup (CASH/
        /// LIABILITY are mapped directly to Cash/Liability without ever entering categoryMap; a
        /// STOCK/FUND symbol somehow missing from a pre-warmed categoryMap falls back to Other
        /// rather than throwing).</summary>
        private static string GroupKey(
            NetWorthGroupBy groupBy, HoldingPosition p,
            Dictionary<string, PortfolioCategory> categoryMap, Dictionary<string, string> subCategoryMap,
            PortfolioCategory fallbackCategory)
        {
            return groupBy switch
            {
                NetWorthGroupBy.Category => p.AssetType switch
                {
                    AssetType.CASH => nameof(PortfolioCategory.Cash),
                    AssetType.LIABILITY => nameof(PortfolioCategory.Liability),
                    AssetType.REAL_ESTATE => nameof(PortfolioCategory.Property),
                    _ => (categoryMap.TryGetValue(p.Symbol, out var cat) ? cat : fallbackCategory).ToString(),
                },
                // Free-form (not the closed PortfolioCategory enum) — CASH/LIABILITY/REAL_ESTATE get
                // a plain Chinese label directly since there's no AI classification to look up for them.
                NetWorthGroupBy.SubCategory => p.AssetType switch
                {
                    AssetType.CASH => "現金",
                    AssetType.LIABILITY => "負債",
                    AssetType.REAL_ESTATE => "不動產",
                    _ => subCategoryMap.TryGetValue(p.Symbol, out var sub) ? sub : "未分類",
                },
                NetWorthGroupBy.Currency => p.Currency == "USD" ? "USD" : "TWD",
                // Verbatim per-holding name for STOCK/FUND; CASH/LIABILITY/REAL_ESTATE collapse into
                // one bucket each (same labels as SubCategory mode) instead of splitting by their
                // free-form Name — multiple cash/liability entries would otherwise clutter the
                // per-holding legend with entries the user doesn't think of as distinct "holdings".
                NetWorthGroupBy.Holding => p.AssetType switch
                {
                    AssetType.CASH => "現金",
                    AssetType.LIABILITY => "負債",
                    AssetType.REAL_ESTATE => "不動產",
                    _ => string.IsNullOrWhiteSpace(p.Name)
                        ? (string.IsNullOrWhiteSpace(p.Symbol) ? "未命名" : p.Symbol)
                        : p.Name,
                },
                _ => p.AssetType switch
                {
                    AssetType.CASH => "Cash",
                    AssetType.LIABILITY => "Liability",
                    // "Property", not "RealEstate": PortfolioCategory.RealEstate (category-mode key)
                    // is REIT stocks/ETFs, a different concept from this literal owned property —
                    // matches Category mode's own key for this AssetType (nameof(Property)) so both
                    // modes render the exact same series identity/color for it.
                    AssetType.REAL_ESTATE => "Property",
                    AssetType.FUND => "Fund",
                    _ => p.Market == "US" ? "UsStock" : "TwStock", // STOCK
                },
            };
        }
    }
}
