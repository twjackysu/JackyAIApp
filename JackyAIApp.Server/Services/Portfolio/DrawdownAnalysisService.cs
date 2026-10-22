using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using static JackyAIApp.Server.Services.Portfolio.PortfolioValuation;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>The portfolio's drawdown curve: how far below its running peak it has been.</summary>
    public interface IDrawdownAnalysisService
    {
        Task<DrawdownAnalysisDto> GetDrawdownAnalysisAsync(string userId, int days, CancellationToken ct = default);
    }

    public class DrawdownAnalysisService(
        IHoldingSnapshotReader snapshotReader,
        IPortfolioPriceLoader priceLoader,
        PortfolioHistoryExtender historyExtender,
        TimeProvider timeProvider) : IDrawdownAnalysisService
    {
        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // ── Drawdown analysis ────────────────────────────────────────────────

        /// <summary>
        /// Max-drawdown analysis over the requested window: daily curve of net worth vs. its running
        /// peak, plus the single worst peak-to-trough episode (magnitude, dates, recovery status).
        /// No groupBy/AI-classification involved, so this isn't subject to
        /// <see cref="NetWorthHistoryService"/>'s 180-day cap (that cap exists for its groupBy modes,
        /// not the price-fetch itself) — allows up to ~3 years so a real drawdown episode isn't cut
        /// off mid-window. Reuses <see cref="ValueAsOfDate"/> (day-change's per-date valuator) instead
        /// of re-deriving the STOCK/FUND/CASH/LIABILITY/REAL_ESTATE valuation rules a third time.
        /// </summary>
        public async Task<DrawdownAnalysisDto> GetDrawdownAnalysisAsync(
            string userId, int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 30, 1095);

            var snapshots = await snapshotReader.AllAsync(userId, ct);
            if (snapshots.Count == 0) return new DrawdownAnalysisDto();

            var today = Today;
            var firstDate = snapshots[0].EffectiveDate;
            var startDate = today.AddDays(-days);
            if (startDate < firstDate) startDate = firstDate;

            var stockSymbols = snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.StockQuote);
            var stockMap = await priceLoader.FetchStocksAsync(stockSymbols, deep: true, ct);
            var depth = await historyExtender.ExtendHistoryAsync(stockMap, snapshots, [], startDate, ct);
            stockMap = depth.Stocks;

            var fundIds = snapshots.SelectMany(s => s.Positions).SymbolsPricedBy(PriceSource.FundNav);
            var fundMap = await priceLoader.FetchFundsAsync(fundIds, ct);
            var asOfPrices = AsOfPriceBook.From(stockMap, fundMap);

            var snapshotIdx = 0;
            var points = new List<DrawdownPointDto>();
            decimal runningPeak = 0;
            var runningPeakDate = startDate;

            for (var date = startDate; date <= today; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;

                while (snapshotIdx + 1 < snapshots.Count && snapshots[snapshotIdx + 1].EffectiveDate <= date)
                    snapshotIdx++;
                var active = snapshots[snapshotIdx].EffectiveDate <= date ? snapshots[snapshotIdx] : null;
                if (active == null) continue;

                var fx = SnapshotFx(active);
                var total = ValueAsOfDate(active.Positions, date, fx, asOfPrices);
                // Null = a held position couldn't be priced as of this date yet (no-data), same gap
                // semantics as GetNetWorthHistoryAsync's own "== 0" skip. A real negative total
                // (debt exceeding assets) is > null and still a valid, kept data point.
                if (total is null) continue;

                if (total.Value > runningPeak)
                {
                    runningPeak = total.Value;
                    runningPeakDate = date;
                }

                var drawdownPct = runningPeak > 0
                    ? Math.Round((total.Value - runningPeak) / runningPeak * 100, 2)
                    : 0m;
                points.Add(new DrawdownPointDto
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    TotalTWD = total.Value,
                    RunningPeakTWD = runningPeak,
                    RunningPeakDate = runningPeakDate.ToString("yyyy-MM-dd"),
                    DrawdownPct = drawdownPct,
                });
            }

            // Still reported with nothing to draw yet: the client is waiting on exactly that.
            if (points.Count == 0) return new DrawdownAnalysisDto { Backfill = depth.Backfill };

            var result = new DrawdownAnalysisDto
            {
                Points = points,
                CurrentDrawdownPct = points[^1].DrawdownPct,
                Backfill = depth.Backfill,
            };

            // Worst single episode: the point with the most negative drawdown (earliest on a tie),
            // then walk forward from its running-peak date to find when total first recovers to that
            // exact peak value again.
            var trough = points.OrderBy(p => p.DrawdownPct).ThenBy(p => p.Date, StringComparer.Ordinal).First();
            if (trough.DrawdownPct == 0)
            {
                result.MaxDrawdownPct = 0;
                return result; // net worth never dipped below a prior peak in this window
            }

            var peakValue = trough.RunningPeakTWD;
            var recoveryPoint = points
                .Where(p => string.CompareOrdinal(p.Date, trough.Date) > 0 && p.TotalTWD >= peakValue)
                .OrderBy(p => p.Date, StringComparer.Ordinal)
                .FirstOrDefault();

            var underwaterEndDate = recoveryPoint?.Date ?? points[^1].Date;
            var underwaterDays = (DateOnly.Parse(underwaterEndDate).ToDateTime(TimeOnly.MinValue)
                - DateOnly.Parse(trough.RunningPeakDate).ToDateTime(TimeOnly.MinValue)).Days;

            result.MaxDrawdownPct = trough.DrawdownPct;
            result.PeakDate = trough.RunningPeakDate;
            result.TroughDate = trough.Date;
            result.RecoveryDate = recoveryPoint?.Date;
            result.UnderwaterDays = underwaterDays;
            result.StillUnderwater = recoveryPoint == null;
            return result;
        }
    }
}
