using JackyAIApp.Server.Data.Models.Portfolio;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Market;

namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Older prices for the long-range charts (drawdown, benchmark comparison): what the background
    /// backfill has stored, added to the ~6 months the providers keep.
    /// </summary>
    public class PortfolioHistoryExtender(
        ILogger<PortfolioHistoryExtender> logger,
        TimeProvider timeProvider,
        IDeepHistoryService deepHistory)
    {
        /// <summary>"Today" for every date walk here — the constructor's <c>timeProvider</c> lets
        /// tests pin a fixed date instead of chasing the real calendar.</summary>
        private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        public sealed record ExtendedHistory(Dictionary<string, DTO.Finance.MarketData> Stocks, HistoryBackfillDto? Backfill);

        /// <summary>How many benchmark symbols one request may put on the backfill queue; the
        /// symbols are typed by the caller, and each is a month-by-month fetch from the exchange.</summary>
        private const int MaxBenchmarkBackfills = 5;

        /// <summary>The first day the prices are needed from, for a stock held from
        /// <paramref name="heldFrom"/>: a few days earlier, so the day it was first held is priced
        /// from the close before it, not from nothing, across a weekend or a holiday.</summary>
        private const int HistoryMarginDays = 10;

        /// <summary>
        /// Adds the older prices that the background backfill has stored to <paramref name="stocks"/>
        /// — the providers' own history stops about six months back — and says how far the
        /// backfill has got. It never fetches anything itself: what is missing is queued for the
        /// background, so a 3-year chart still comes back as fast as a 3-month one, drawn from what
        /// is stored so far. Fail-soft like the Portfolio services: if the extension fails, the
        /// chart is drawn from the prices that were already in hand.
        /// </summary>
        public async Task<ExtendedHistory> ExtendHistoryAsync(
            Dictionary<string, DTO.Finance.MarketData> stocks, IReadOnlyList<HoldingSnapshot> snapshots,
            IReadOnlyCollection<string> benchmarkSymbols, DateOnly startDate, CancellationToken ct)
        {
            // A window the providers already cover needs nothing from this.
            var deepWindowStart = Today.AddMonths(-(PersistentTwHistoricalProvider.LookbackMonths - 1));
            if (startDate >= deepWindowStart) return new ExtendedHistory(stocks, null);

            try
            {
                var wants = HistoryWants(snapshots, benchmarkSymbols, startDate);
                var result = await deepHistory.ExtendAsync(stocks, wants, startDate, ct);
                var status = result.Backfill;
                return new ExtendedHistory(result.Stocks, status is null ? null : new HistoryBackfillDto
                {
                    State = status.State,
                    CoveredFrom = status.CoveredFrom.ToString("yyyy-MM-dd"),
                    TargetFrom = status.TargetFrom.ToString("yyyy-MM-dd"),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not extend price history back to {StartDate}; using the prices in hand", startDate);
                return new ExtendedHistory(stocks, null);
            }
        }

        /// <summary>What each stock in the snapshots (and each benchmark) needs of its history to
        /// price the window from <paramref name="startDate"/>. A held stock is priced from the day it
        /// was first held, so there is no reason to fetch the years before that; a benchmark is
        /// priced for the whole window.</summary>
        internal static List<HistoryWant> HistoryWants(
            IReadOnlyList<HoldingSnapshot> snapshots, IReadOnlyCollection<string> benchmarkSymbols, DateOnly startDate)
        {
            var heldFrom = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
            foreach (var snapshot in snapshots)
                foreach (var position in snapshot.Positions)
                {
                    // Only stock quotes are backfilled; a fund's NAV history comes whole from FundRich.
                    if (AssetValuationPolicy.For(position.AssetType).PriceSource != PriceSource.StockQuote
                        || string.IsNullOrWhiteSpace(position.Symbol)) continue;
                    if (!heldFrom.TryGetValue(position.Symbol, out var first) || snapshot.EffectiveDate < first)
                        heldFrom[position.Symbol] = snapshot.EffectiveDate;
                }

            var benchmarks = benchmarkSymbols.Take(MaxBenchmarkBackfills).ToHashSet(StringComparer.Ordinal);
            var wants = new List<HistoryWant>();
            foreach (var symbol in heldFrom.Keys.Concat(benchmarks).Distinct(StringComparer.Ordinal))
            {
                DateOnly? held = heldFrom.TryGetValue(symbol, out var from) ? from : null;
                var needFrom = benchmarks.Contains(symbol) || held is null
                    ? startDate
                    : Max(startDate, held.Value.AddDays(-HistoryMarginDays));
                wants.Add(new HistoryWant(symbol, needFrom, held));
            }
            return wants;
        }

        private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;
    }
}
