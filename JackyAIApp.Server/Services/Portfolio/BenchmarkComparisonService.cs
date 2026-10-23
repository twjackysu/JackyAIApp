using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using static JackyAIApp.Server.Services.Portfolio.PortfolioValuation;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>The portfolio's time-weighted return against benchmark symbols over the same window.</summary>
    public interface IBenchmarkComparisonService
    {
        Task<BenchmarkComparisonDto> GetBenchmarkComparisonAsync(
            string userId, int days, IReadOnlyList<string>? benchmarkSymbols = null, CancellationToken ct = default);
    }

    public class BenchmarkComparisonService(
        IHoldingSnapshotReader snapshotReader,
        IPortfolioPriceLoader priceLoader,
        IDividendService dividendData,
        PortfolioHistoryExtender historyExtender,
        TimeProvider timeProvider) : IBenchmarkComparisonService
    {
        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        private static readonly string[] DefaultBenchmarkSymbols = ["0050"];

        /// <summary>
        /// "What if every contribution I made had bought each of <paramref name="benchmarkSymbols"/>
        /// instead of what I actually bought?" A contribution is a change in the recorded cost basis of
        /// the cost-tracked positions from one snapshot to the next — the same proxy as the summary's XIRR,
        /// so the two agree. It is the same for every benchmark; each buys at its own price (see
        /// <see cref="BenchmarkLeg"/>), and the portfolio side is the market value of those same positions,
        /// so every curve is directly comparable.
        ///
        /// The percentages are 含息 on both sides: price-only would penalise a high-yield portfolio, whose
        /// distributions knock its price down on each ex-date while a lower-yielding 0050 gives up less.
        /// Dividends are income retained, as 總覽's 總報酬 and XIRR count them. The curves stay pure market
        /// value, so they reconcile against a brokerage statement — which is why a line's rise and its
        /// headline % are not expected to match.
        /// </summary>
        public async Task<BenchmarkComparisonDto> GetBenchmarkComparisonAsync(
            string userId, int days, IReadOnlyList<string>? benchmarkSymbols = null, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 30, 1095);
            var symbols = ResolveBenchmarkSymbols(benchmarkSymbols);

            var snapshots = await snapshotReader.AllAsync(userId, ct);
            if (snapshots.Count == 0) return Unpriced(symbols, backfill: null);

            var today = Today;
            var startDate = Max(today.AddDays(-days), snapshots[0].EffectiveDate);

            var stockSymbols = snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.StockQuote);
            foreach (var s in symbols)
                if (!stockSymbols.Contains(s)) stockSymbols.Add(s);
            var stockMap = await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct);
            var depth = await historyExtender.ExtendHistoryAsync(stockMap, snapshots, symbols, startDate, ct);
            stockMap = depth.Stocks;

            // A benchmark that could not be priced is dropped rather than failing the request: the UI shows
            // whichever came back.
            var priced = symbols.Where(s => stockMap.TryGetValue(s, out var data) && data.HistoricalPrices.Count > 0).ToList();
            if (priced.Count == 0) return Unpriced(symbols, depth.Backfill);

            var fundIds = snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.FundNav);
            var fundMap = await priceLoader.FetchFundsAsync(fundIds, ct);
            var portfolioDividends = await CostTrackedDividendsByDateAsync(snapshots, fundMap, startDate, today, ct);
            var legs = await Task.WhenAll(priced.Select(async s => new BenchmarkLeg(
                s,
                ForwardPriceCursor.ForStock(stockMap[s].HistoricalPrices),
                DividendPerUnitByDate(await dividendData.GetStockDividendsAsync(s, MarketOf(s), ct)))));
            // Sorted once per request; it also holds the benchmarks, which the portfolio side never asks about.
            var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);

            // What was invested before the window counts as bought at the window's start price — the same
            // baseline as the portfolio side, which simply values those holdings on that day. Otherwise the
            // first snapshot inside the window would "buy" the whole cumulative cost on its own date.
            var investedBefore = snapshots.LastOrDefault(s => s.EffectiveDate <= startDate);
            var previousCost = investedBefore != null ? CostTrackedCostTWD(investedBefore) : 0;
            if (previousCost > 0)
                foreach (var leg in legs) leg.Seed(previousCost, startDate);

            var dates = new List<string>();
            var portfolioValues = new List<decimal>();
            var portfolioReturn = new TimeWeightedReturn();
            // Income and contributions not yet in a recorded day's growth. A day may not be recorded (a weekend,
            // a day something has no price), and what happened on it must still count, once, on the next one.
            decimal portfolioIncome = 0, contribution = 0;
            var snapshotsInEffect = new SnapshotCursor(snapshots);
            // The snapshot whose cost is in previousCost.
            var applied = investedBefore;

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                // Before any skip, and before the day's contribution: a distribution is earned by what was
                // held going into its ex-date.
                portfolioIncome += portfolioDividends.GetValueOrDefault(date);
                foreach (var leg in legs) leg.AccrueDividends(date);

                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (snapshotsInEffect.ActiveOn(date) is not { } active) continue;

                // A snapshot's contribution applies once, the first day it is found in effect — the day it takes
                // effect, or the next weekday for one dated on a weekend — before the benchmarks are priced for
                // that day, as the portfolio side prices its new positions that same day.
                if (!ReferenceEquals(active, applied))
                {
                    var cost = CostTrackedCostTWD(active);
                    var change = cost - previousCost;
                    previousCost = cost;
                    applied = active;
                    contribution += change;
                    if (change != 0)
                        foreach (var leg in legs) leg.Invest(change, date);
                }

                // A day counts only when the portfolio and every benchmark have a price for it.
                if (CostTrackedValueAsOf(active, date, asOfPrices) is not { } portfolioValue) continue;
                if (legs.Any(leg => leg.PriceOn(date) <= 0)) continue;

                dates.Add(date.ToString("yyyy-MM-dd"));
                portfolioValues.Add(portfolioValue);
                foreach (var leg in legs) leg.Record(date, contribution);
                portfolioReturn.Record(portfolioValue, contribution, portfolioIncome);
                portfolioIncome = 0;
                contribution = 0;
            }

            var result = new BenchmarkComparisonDto
            {
                Dates = dates,
                PortfolioTWD = portfolioValues,
                Benchmarks = legs.Select(leg => new BenchmarkSeriesDto { Symbol = leg.Symbol, ValuesTWD = leg.Values }).ToList(),
                Backfill = depth.Backfill,
            };
            if (dates.Count == 0) return result;

            result.PortfolioReturnPct = portfolioReturn.Pct;
            for (var i = 0; i < legs.Length; i++)
                result.Benchmarks[i].ReturnPct = legs[i].Return.Pct;
            return result;
        }

        /// <summary>The requested symbols, trimmed, upper-cased and distinct; 0050 when none are left.</summary>
        private static List<string> ResolveBenchmarkSymbols(IReadOnlyList<string>? requested)
        {
            var symbols = (requested == null || requested.Count == 0
                    ? DefaultBenchmarkSymbols
                    : requested.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0))
                .Distinct()
                .ToList();
            return symbols.Count > 0 ? symbols : [.. DefaultBenchmarkSymbols];
        }

        private static BenchmarkComparisonDto Unpriced(IEnumerable<string> symbols, HistoryBackfillDto? backfill) => new()
        {
            Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList(),
            Backfill = backfill,
        };

        /// <summary>The benchmark presets come in two shapes: TW codes lead with a digit (0050), US tickers do not (QQQ).</summary>
        private static string MarketOf(string symbol) => char.IsDigit(symbol[0]) ? "TW" : "US";

        private static Dictionary<DateOnly, decimal> DividendPerUnitByDate(IEnumerable<DividendRecord> records) =>
            records.GroupBy(r => r.ExDate).ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPerUnit));

        /// <summary>
        /// What the cost-tracked positions cost, in TWD at the snapshot's rate. Its change from one snapshot to
        /// the next is a contribution or a withdrawal.
        /// </summary>
        private static decimal CostTrackedCostTWD(HoldingSnapshot snapshot) =>
            snapshot.Positions
                .Where(p => AssetValuationPolicy.For(p.AssetType).HasCostBasis && p.CostBasis.HasValue)
                .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD" ? SnapshotFx(snapshot) : 1m));

        /// <summary>
        /// The market value on <paramref name="date"/> of the positions with a recorded cost — the summary
        /// XIRR's universe. A position without a cost has no contribution driving the benchmark side, so
        /// counting it would drift the curves apart for reasons that are not performance. Null when a stock
        /// has no price for the day (the day is unknown); a fund without one is left out.
        /// </summary>
        private static decimal? CostTrackedValueAsOf(HoldingSnapshot snapshot, DateOnly date, AsOfPriceBook prices)
        {
            var fx = SnapshotFx(snapshot);
            decimal total = 0;
            foreach (var p in snapshot.Positions)
            {
                if (!p.CostBasis.HasValue) continue;
                var policy = AssetValuationPolicy.For(p.AssetType);
                if (policy.PriceAsOf(p, date, prices) is not { } asOf)
                {
                    if (policy.PriceSource == PriceSource.StockQuote) return null;
                    continue;
                }
                // No NAV on or before the day falls back to the saved unit price; a NAV that exists but is
                // <= 0 does not — hence Close ?? StoredPrice, not a "<= 0" test.
                var price = asOf.Close ?? policy.StoredPrice(p) ?? 0;
                if (price <= 0)
                {
                    if (policy.PriceSource == PriceSource.StockQuote) return null;
                    continue;
                }
                total += price * p.Quantity * (asOf.Currency == "USD" ? fx : 1m);
            }
            return total;
        }

        /// <summary>
        /// Per-ex-date TWD dividends earned by the COST-TRACKED stock/fund positions only, for the
        /// window [startDate, today]. Deliberately not PortfolioService's <c>ComputeDividends</c>: that one
        /// covers every position whether or not a cost basis was ever filled in, and the benchmark
        /// comparison prices only cost-tracked holdings on both sides — the same universe argument as
        /// <see cref="CostTrackedValueAsOf"/>.
        ///
        /// Quantity and FX are read from the snapshot active on the ex-date, so a dividend counts
        /// against what was actually held when it was paid, not today's position.
        /// </summary>
        private async Task<Dictionary<DateOnly, decimal>> CostTrackedDividendsByDateAsync(
            List<HoldingSnapshot> orderedSnapshots, Dictionary<string, FundNavHistory> fundMap,
            DateOnly startDate, DateOnly today, CancellationToken ct)
        {
            var byDate = new Dictionary<DateOnly, decimal>();

            var keys = orderedSnapshots
                .SelectMany(s => s.Positions)
                .Where(p => AssetValuationPolicy.For(p.AssetType).PaysDividends
                    && p.CostBasis.HasValue && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => (p.AssetType, Symbol: p.Symbol, Market: p.Market))
                .Distinct()
                .ToList();
            if (keys.Count == 0) return byDate;

            var fetched = await Task.WhenAll(keys.Select(async key => (
                Key: key,
                Records: await dividendData.GetDividendsAsync(key.AssetType, key.Symbol, key.Market, ct)
            )));

            foreach (var (key, records) in fetched)
            {
                foreach (var record in records)
                {
                    if (record.ExDate < startDate || record.ExDate > today) continue;

                    var snap = orderedSnapshots.LastOrDefault(s => s.EffectiveDate <= record.ExDate);
                    var position = snap?.Positions.FirstOrDefault(p =>
                        p.AssetType == key.AssetType && p.Symbol == key.Symbol && p.Market == key.Market);
                    if (snap == null || position == null || position.Quantity <= 0 || !position.CostBasis.HasValue)
                        continue;

                    var currency = AssetValuationPolicy.For(key.AssetType).DividendCurrency(record, key.Symbol, fundMap);
                    var fxMul = currency == "USD" ? SnapshotFx(snap) : 1m;

                    byDate[record.ExDate] = byDate.GetValueOrDefault(record.ExDate)
                        + position.Quantity * record.AmountPerUnit * fxMul;
                }
            }

            return byDate;
        }

        private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
    }
}
