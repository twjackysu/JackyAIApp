using JackyAIApp.Server.Common;
using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Finance;
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
        TimeProvider timeProvider) : INetWorthHistoryService
    {
        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

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
            if (startDate < snapshots[0].EffectiveDate) startDate = snapshots[0].EffectiveDate;

            // Deep: the day loop walks the whole window for every symbol.
            var stocks = await priceLoader.FetchStocksAsync(
                snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.StockQuote), deep: true, ct);
            var funds = await priceLoader.FetchFundsAsync(
                snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.FundNav), ct);
            var grouping = await NetWorthGrouping.LoadAsync(groupBy, snapshots, categoryService, ct);
            var values = new NetWorthDayValuation(stocks, funds);
            var snapshotsInEffect = new SnapshotCursor(snapshots);

            var result = new List<NetWorthDataPoint>();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (snapshotsInEffect.ActiveOn(date) is not { } active) continue;

                var point = PointOn(date, active, values, grouping);
                // Exactly 0 is a day where nothing could be priced: no data. A negative total (debt over
                // assets) is a real point.
                if (point.TotalTWD == 0) continue;
                result.Add(point);
            }
            return result;
        }

        private static NetWorthDataPoint PointOn(
            DateOnly date, HoldingSnapshot active, NetWorthDayValuation values, NetWorthGrouping grouping)
        {
            var fx = SnapshotFx(active);
            var groups = new Dictionary<string, decimal>();
            decimal total = 0;
            foreach (var p in active.Positions)
            {
                if (values.ValueOn(p, date, fx) is not { } valueTWD) continue;
                var key = grouping.KeyFor(p);
                groups[key] = groups.GetValueOrDefault(key) + valueTWD;
                total += valueTWD;
            }
            return new NetWorthDataPoint
            {
                Date = date.ToString("yyyy-MM-dd"),
                TotalTWD = total,
                Groups = groups.Select(kv => new NetWorthGroupValue { Key = kv.Key, ValueTWD = kv.Value }).ToList(),
            };
        }
    }

    /// <summary>
    /// A position's value on one day of the net worth history, by this screen's own rules for a missing
    /// price (the screens differ on purpose, see <c>PortfolioAsOfValuationTests</c>). An amount counts as
    /// itself, negative for a debt. A fund is valued at its last NAV on or before the day, else at the unit
    /// price saved with it (a NAV of 0 or below counts as missing), converted by the NAV's currency once it
    /// has a NAV history at all. A stock is valued at its last close on or before the day, else not at all.
    /// </summary>
    internal sealed class NetWorthDayValuation(
        IReadOnlyDictionary<string, MarketData> stocks, IReadOnlyDictionary<string, FundNavHistory> funds)
    {
        private readonly Dictionary<string, ForwardPriceCursor<DateOnly>> _stockPrices =
            stocks.ToDictionary(kv => kv.Key, kv => ForwardPriceCursor.ForStock(kv.Value.HistoricalPrices));
        private readonly Dictionary<string, ForwardPriceCursor<string>> _fundNavs =
            funds.ToDictionary(kv => kv.Key, kv => ForwardPriceCursor.ForFund(kv.Value.Prices));

        /// <summary>In TWD at the snapshot's <paramref name="fx"/>; null when it has no value that day.
        /// Days in non-decreasing order.</summary>
        public decimal? ValueOn(HoldingPosition p, DateOnly date, decimal fx)
        {
            var fxMul = p.Currency == "USD" ? fx : 1m;
            var policy = AssetValuationPolicy.For(p.AssetType);

            // policy.Sign is the one place a value turns negative (a debt): the total just adds.
            if (!policy.IsMarketPriced) return policy.Sign * p.Quantity * fxMul;

            if (policy.PriceSource == PriceSource.FundNav)
            {
                var nav = p.UnitPrice ?? 0;
                if (!string.IsNullOrWhiteSpace(p.Symbol) && funds.TryGetValue(p.Symbol, out var history))
                {
                    fxMul = history.Currency == "USD" ? fx : 1m;
                    if (_fundNavs[p.Symbol].CloseOnOrBefore(date.ToString("yyyy-MM-dd")) is decimal close && close > 0)
                        nav = close;
                }
                return nav > 0 ? nav * p.Quantity * fxMul : null;
            }

            return _stockPrices.TryGetValue(p.Symbol, out var prices) && prices.CloseOnOrBefore(date) is decimal last && last > 0
                ? last * p.Quantity * fxMul
                : null;
        }
    }

    /// <summary>
    /// How the net worth history groups positions into series. The category modes need each stock or fund
    /// classified (an AI call per distinct symbol, mostly cache hits — StockCategoryService persists them),
    /// so those are resolved once, all at once, and only for those modes.
    /// </summary>
    internal sealed class NetWorthGrouping
    {
        private readonly NetWorthGroupBy _groupBy;
        private readonly Dictionary<string, PortfolioCategory> _categories;
        private readonly Dictionary<string, string> _subCategories;

        private NetWorthGrouping(
            NetWorthGroupBy groupBy, Dictionary<string, PortfolioCategory> categories, Dictionary<string, string> subCategories)
        {
            _groupBy = groupBy;
            _categories = categories;
            _subCategories = subCategories;
        }

        public static async Task<NetWorthGrouping> LoadAsync(
            NetWorthGroupBy groupBy, IEnumerable<HoldingSnapshot> snapshots, IStockCategoryService categoryService, CancellationToken ct)
        {
            if (groupBy is not (NetWorthGroupBy.Category or NetWorthGroupBy.SubCategory))
                return new NetWorthGrouping(groupBy, [], []);

            var classifiable = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => AssetValuationPolicy.For(p.AssetType).FixedCategory is null && !string.IsNullOrWhiteSpace(p.Symbol))
                .GroupBy(p => p.Symbol)
                .Select(g => g.Last()) // the most recent name/market for the symbol
                .ToList();
            var resolved = await Task.WhenAll(classifiable.Select(async p =>
            {
                var (category, subCategory, _) = await categoryService.GetCategoryAsync(p.AssetType, p.Symbol, p.Name, p.Market, ct);
                return (p.Symbol, Category: category, SubCategory: subCategory);
            }));
            return new NetWorthGrouping(
                groupBy,
                resolved.ToDictionary(r => r.Symbol, r => r.Category),
                // No sub-category (classification failed, or genuinely other): a named bucket, not an empty key.
                resolved.ToDictionary(r => r.Symbol, r => string.IsNullOrWhiteSpace(r.SubCategory) ? "未分類" : r.SubCategory));
        }

        /// <summary>
        /// The series a position belongs to. A type with a fixed category or label (cash, debt, real estate)
        /// never needs a classification; a stock or fund missing from the classifications is Other rather than
        /// an error.
        /// </summary>
        public string KeyFor(HoldingPosition p)
        {
            var policy = AssetValuationPolicy.For(p.AssetType);
            return _groupBy switch
            {
                NetWorthGroupBy.Category => (policy.FixedCategory
                    ?? (_categories.TryGetValue(p.Symbol, out var category) ? category : PortfolioCategory.Other)).ToString(),
                // Free-form (not the closed PortfolioCategory enum): a fixed-label type gets its plain label.
                NetWorthGroupBy.SubCategory => policy.FixedLabel
                    ?? (_subCategories.TryGetValue(p.Symbol, out var sub) ? sub : "未分類"),
                NetWorthGroupBy.Currency => p.Currency == "USD" ? "USD" : "TWD",
                // Each stock or fund by name; a fixed-label type is one bucket (the SubCategory label), so several
                // cash or debt entries do not clutter the legend as if they were separate holdings.
                NetWorthGroupBy.Holding => policy.FixedLabel
                    ?? (string.IsNullOrWhiteSpace(p.Name)
                        ? (string.IsNullOrWhiteSpace(p.Symbol) ? "未命名" : p.Symbol)
                        : p.Name),
                // Real estate is "Property", the same key as its Category mode, so both modes give it the same
                // series identity and colour.
                _ => policy.BucketOf(p).ToString(),
            };
        }
    }
}
