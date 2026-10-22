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
        TimeProvider? timeProvider = null) : IBenchmarkComparisonService
    {
        private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // ── Benchmark comparison ─────────────────────────────────────────────

        private static readonly string[] DefaultBenchmarkSymbols = ["0050"];

        /// <summary>
        /// "What if every contribution I made had bought each of <paramref name="benchmarkSymbols"/>
        /// instead of what I actually bought?" Reuses ComputeAnnualizedReturn's exact cash-flow
        /// proxy — Σ CostBasis of non-cash positions, delta vs. the previous snapshot = a dated
        /// contribution/withdrawal — so this stays consistent with the XIRR figure already shown
        /// elsewhere: same universe (cost-tracked STOCK/FUND only), same "what changed in recorded
        /// cost basis is what got added/pulled" assumption. That contribution flow doesn't depend on
        /// which benchmark it's compared against, so it's computed once per date and applied to
        /// every requested benchmark independently (each buys/holds units at its own daily price —
        /// symbols never affect each other's curve). The portfolio side is priced the same way, so
        /// every curve here is a directly comparable curve.
        ///
        /// The reported percentages are 含息 (total return) on BOTH sides. Price-only would
        /// systematically penalise a high-yield portfolio: a distribution knocks the price down on
        /// its ex-date, so 0056/00878-style holdings would show that drop as a loss while a lower-
        /// yielding 0050 gives up less price — scoring the user against a benchmark that is handed
        /// an advantage purely by paying out less. Dividends are counted as income retained (added
        /// to the growth factor on the day earned, not reinvested into more units), matching how
        /// 總覽's 總報酬 and XIRR already treat them. The plotted CURVES stay pure market value, so
        /// they still reconcile against a brokerage statement — which is why the line's rise and
        /// the headline % are not expected to match.
        /// </summary>
        public async Task<BenchmarkComparisonDto> GetBenchmarkComparisonAsync(
            string userId, int days, IReadOnlyList<string>? benchmarkSymbols = null, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 30, 1095);
            var symbols = (benchmarkSymbols == null || benchmarkSymbols.Count == 0
                    ? DefaultBenchmarkSymbols
                    : benchmarkSymbols.Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0))
                .Distinct()
                .ToList();
            if (symbols.Count == 0) symbols = [.. DefaultBenchmarkSymbols];

            var snapshots = await snapshotReader.AllAsync(userId, ct);
            if (snapshots.Count == 0)
                return new BenchmarkComparisonDto { Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList() };

            var today = Today;
            var firstDate = snapshots[0].EffectiveDate;
            var startDate = today.AddDays(-days);
            if (startDate < firstDate) startDate = firstDate;

            var stockSymbols = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.STOCK)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            foreach (var s in symbols)
                if (!stockSymbols.Contains(s)) stockSymbols.Add(s);
            var stockMap = await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct);
            var depth = await historyExtender.ExtendHistoryAsync(stockMap, snapshots, symbols, startDate, ct);
            stockMap = depth.Stocks;

            // Symbols that failed to price (unknown/unpriceable) are silently dropped rather than
            // failing the whole request — the UI shows whichever requested benchmarks actually came
            // back, same fail-soft spirit as the rest of this class.
            var benchmarkPriceLists = symbols
                .Select(s => (Symbol: s,
                    Prices: stockMap.TryGetValue(s, out var data) ? data.HistoricalPrices.OrderBy(x => x.Date).ToList() : []))
                .Where(b => b.Prices.Count > 0)
                .ToList();
            if (benchmarkPriceLists.Count == 0)
                return new BenchmarkComparisonDto
                {
                    Benchmarks = symbols.Select(s => new BenchmarkSeriesDto { Symbol = s }).ToList(),
                    Backfill = depth.Backfill,
                };

            var cursors = new int[benchmarkPriceLists.Count];
            var units = new decimal[benchmarkPriceLists.Count];

            decimal BenchmarkPriceAsOf(int idx, DateOnly date)
            {
                var dt = date.ToDateTime(TimeOnly.MinValue);
                var prices = benchmarkPriceLists[idx].Prices;
                while (cursors[idx] + 1 < prices.Count && prices[cursors[idx] + 1].Date.Date <= dt)
                    cursors[idx]++;
                return prices[cursors[idx]].Date.Date <= dt ? prices[cursors[idx]].Close : 0;
            }

            var fundIds = snapshots
                .SelectMany(s => s.Positions)
                .Where(p => p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol))
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();
            var fundMap = await priceLoader.FetchFundsAsync(fundIds, ct);

            // 含息 inputs for both sides (see the method's remarks for why price-only is unfair to
            // a high-yield portfolio). Benchmark market is inferred from the symbol shape, matching
            // the only two shapes this app's benchmark presets take: TW codes are numeric-leading
            // (0050), US tickers are not (QQQ/VOO/VT).
            var portfolioDivByDate =
                await CostTrackedDividendsByDateAsync(snapshots, fundMap, startDate, today, ct);
            var benchmarkDivRecords = await Task.WhenAll(benchmarkPriceLists.Select(b =>
                dividendData.GetStockDividendsAsync(
                    b.Symbol, char.IsDigit(b.Symbol[0]) ? "TW" : "US", ct)));
            var benchmarkDivByDate = benchmarkDivRecords
                .Select(records => records
                    .GroupBy(r => r.ExDate)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.AmountPerUnit)))
                .ToArray();

            // Sorted once per request (not per day × position); `stockMap` also holds the benchmark
            // symbols, which this valuation never asks about — a few extra sorts, not worth a filter.
            var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);

            // Same cost-tracked universe as ComputeAnnualizedReturn's terminal value (costBasisTWD +
            // pnlTWD == market value of positions that actually have a recorded cost) — a position
            // with no cost basis filled in has no matching contribution flow driving the benchmark
            // side, so including it here would drift the two curves apart for reasons unrelated to
            // performance.
            decimal? InvestedValueAsOfDate(HoldingSnapshot snap, DateOnly date)
            {
                var dayKey = date.ToString("yyyy-MM-dd");
                var fx = SnapshotFx(snap);
                decimal total = 0;
                foreach (var p in snap.Positions)
                {
                    if (!p.CostBasis.HasValue) continue;
                    if (p.AssetType == AssetType.STOCK)
                    {
                        if (!asOfPrices.Stocks.TryGetValue(p.Symbol, out var series)) return null;
                        var price = series.CloseOnOrBefore(date) ?? 0;
                        if (price <= 0) return null;
                        total += price * p.Quantity * (p.Currency == "USD" ? fx : 1m);
                    }
                    else if (p.AssetType == AssetType.FUND && !string.IsNullOrWhiteSpace(p.Symbol) &&
                             asOfPrices.Funds.TryGetValue(p.Symbol, out var fund))
                    {
                        // No NAV on or before the day falls back to the saved unit price; a NAV that
                        // exists but is <= 0 does not (it skips the position) — hence the nullable.
                        var price = fund.Prices.CloseOnOrBefore(dayKey) ?? (p.UnitPrice ?? 0);
                        if (price <= 0) continue;
                        total += price * p.Quantity * (fund.Currency == "USD" ? fx : 1m);
                    }
                }
                return total;
            }

            var snapshotIdx = 0;

            // Seed the running cost-basis + benchmark unit count with whatever was already
            // invested before the window starts, valued at the window's own start-date price —
            // the same baseline InvestedValueAsOfDate implicitly uses for the portfolio side (it
            // just returns real market value at that date, regardless of when it was actually
            // bought). Without this, a snapshot dated before startDate is invisible to the day
            // loop below (it only reacts to `date == active.EffectiveDate`, which never fires for
            // a pre-window date), so the NEXT snapshot that happens to land inside the window
            // would wrongly "buy" its entire cumulative cost basis at that one later date instead
            // of just its own incremental delta — badly distorting the simulated return.
            var priorSnapshot = snapshots.LastOrDefault(s => s.EffectiveDate <= startDate);
            var prevCost = priorSnapshot?.Positions
                .Where(p => AssetValuationPolicy.For(p.AssetType).HasCostBasis && p.CostBasis.HasValue)
                .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD"
                    ? SnapshotFx(priorSnapshot) : 1m)) ?? 0;
            if (prevCost > 0)
            {
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var startPrice = BenchmarkPriceAsOf(i, startDate);
                    if (startPrice > 0) units[i] = prevCost / startPrice;
                }
            }

            var dates = new List<string>();
            var portfolioValues = new List<decimal>();
            var benchmarkValues = new List<decimal>[benchmarkPriceLists.Count];
            for (var i = 0; i < benchmarkPriceLists.Count; i++) benchmarkValues[i] = [];

            // Time-weighted return accumulators. A plain (last - first) / first over these curves
            // would count deposits as gains: both curves grow when money is added, so an account
            // that contributed 2.7M into a 7M portfolio reads as "+31%" having earned nothing.
            // TWR chains each period's growth factor with that period's contribution removed from
            // the base, which is exactly what makes a portfolio comparable to a benchmark.
            decimal portfolioTwr = 1m;
            var benchmarkTwr = new decimal[benchmarkPriceLists.Count];
            for (var i = 0; i < benchmarkTwr.Length; i++) benchmarkTwr[i] = 1m;
            decimal? prevPortfolioValue = null;
            var prevBenchmarkValue = new decimal?[benchmarkPriceLists.Count];
            // The contribution applied on the day currently being priced; already reflected in
            // both this day's portfolio value and this day's benchmark units.
            decimal contributionToday = 0;
            // Dividends earned but not yet folded into a growth factor. An ex-date can land on a
            // day this loop never records (a weekend, or a day where some symbol has no price), and
            // income dropped on such a day would silently vanish from the return — so it accrues
            // here and is applied on the next day that is actually recorded.
            decimal pendingPortfolioDiv = 0;
            var pendingBenchmarkDiv = new decimal[benchmarkPriceLists.Count];

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                // Accrued before every `continue` below, and before this day's contribution is
                // applied: a distribution is earned by the units held going INTO the ex-date, so
                // yesterday's unit count is the correct one to value it at.
                pendingPortfolioDiv += portfolioDivByDate.GetValueOrDefault(date);
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                    pendingBenchmarkDiv[i] += units[i] * benchmarkDivByDate[i].GetValueOrDefault(date);

                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                // Apply this snapshot's contribution/withdrawal exactly once, the day it takes
                // effect, before pricing each benchmark for that same day — so a same-day
                // contribution is reflected in that day's benchmark values, matching how the
                // portfolio side already reflects it (the snapshot's positions price immediately).
                // The contribution amount is benchmark-independent; only the buy price (and hence
                // units bought) differs per symbol.
                contributionToday = 0;
                if (date == active.EffectiveDate)
                {
                    var snapCost = active.Positions
                        .Where(p => AssetValuationPolicy.For(p.AssetType).HasCostBasis && p.CostBasis.HasValue)
                        .Sum(p => p.CostBasis!.Value * ((p.CostCurrency ?? p.Currency) == "USD"
                            ? SnapshotFx(active) : 1m));
                    var delta = snapCost - prevCost;
                    prevCost = snapCost;
                    contributionToday = delta;

                    if (delta != 0)
                    {
                        for (var i = 0; i < benchmarkPriceLists.Count; i++)
                        {
                            var buyPrice = BenchmarkPriceAsOf(i, date);
                            if (buyPrice > 0) units[i] = Math.Max(0, units[i] + delta / buyPrice);
                        }
                    }
                }

                var portfolioTotal = InvestedValueAsOfDate(active, date);
                if (portfolioTotal is null) continue;

                // Keep every array index-aligned: a date only counts if the portfolio AND every
                // requested benchmark all have a price for it.
                var pricesToday = new decimal[benchmarkPriceLists.Count];
                var allPriced = true;
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var price = BenchmarkPriceAsOf(i, date);
                    if (price <= 0) { allPriced = false; break; }
                    pricesToday[i] = price;
                }
                if (!allPriced) continue;

                dates.Add(date.ToString("yyyy-MM-dd"));
                portfolioValues.Add(portfolioTotal.Value);
                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                    benchmarkValues[i].Add(Math.Round(units[i] * pricesToday[i], 2));

                // Chain this day's growth factor, with today's contribution added to the opening
                // base so the deposit itself never counts as a gain. A base at or below zero has
                // no meaningful growth factor, so that day is skipped rather than allowed to
                // produce a divide-by-zero or a sign flip.
                // Dividends are added to the numerator only: the income counts as return on the
                // day it lands, then leaves the base (it is cash in hand, not reinvested units),
                // so it is credited exactly once.
                var portfolioBase = (prevPortfolioValue ?? 0) + contributionToday;
                if (prevPortfolioValue != null && portfolioBase > 0)
                    portfolioTwr *= (portfolioTotal.Value + pendingPortfolioDiv) / portfolioBase;
                prevPortfolioValue = portfolioTotal.Value;
                pendingPortfolioDiv = 0;

                for (var i = 0; i < benchmarkPriceLists.Count; i++)
                {
                    var todayValue = units[i] * pricesToday[i];
                    var benchBase = (prevBenchmarkValue[i] ?? 0) + contributionToday;
                    if (prevBenchmarkValue[i] != null && benchBase > 0)
                        benchmarkTwr[i] *= (todayValue + pendingBenchmarkDiv[i]) / benchBase;
                    prevBenchmarkValue[i] = todayValue;
                    pendingBenchmarkDiv[i] = 0;
                }
            }

            var result = new BenchmarkComparisonDto
            {
                Dates = dates,
                PortfolioTWD = portfolioValues,
                Benchmarks = benchmarkPriceLists.Select((b, i) => new BenchmarkSeriesDto
                {
                    Symbol = b.Symbol,
                    ValuesTWD = benchmarkValues[i],
                }).ToList(),
                Backfill = depth.Backfill,
            };
            if (dates.Count == 0) return result;

            // Time-weighted, so these are genuine returns rather than "how much the pot grew" —
            // deposits move both curves but must not appear as performance. This is also what
            // makes the two figures directly comparable to each other.
            result.PortfolioReturnPct = Math.Round((portfolioTwr - 1m) * 100, 2);
            for (var i = 0; i < result.Benchmarks.Count; i++)
                result.Benchmarks[i].ReturnPct = Math.Round((benchmarkTwr[i] - 1m) * 100, 2);
            return result;
        }

        /// <summary>
        /// Per-ex-date TWD dividends earned by the COST-TRACKED stock/fund positions only, for the
        /// window [startDate, today]. Deliberately not PortfolioService's <c>ComputeDividends</c>: that one
        /// covers every position whether or not a cost basis was ever filled in, and the benchmark
        /// comparison prices only cost-tracked holdings on both sides. Crediting the portfolio curve
        /// with income from a position that contributes nothing to the benchmark side would drift
        /// the two apart for reasons unrelated to performance — the same universe argument that
        /// governs InvestedValueAsOfDate.
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

                    // FundRich's dividend history has no currency field; fall back to the fund's NAV currency.
                    var currency = key.AssetType == AssetType.FUND
                        ? (fundMap.TryGetValue(key.Symbol, out var nav) ? nav.Currency : "TWD")
                        : record.Currency;
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
