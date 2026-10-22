using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Market.US;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Finance.Prediction
{
    public class PredictionEvaluationService(
        IDbContextFactory<AzureSQLDBContext> dbContextFactory,
        IMarketDataProviderFactory providerFactory,
        ITWStockLibHistoricalProvider twHistoricalProvider,
        YahooFinanceMarketDataProvider yahooProvider,
        ILogger<PredictionEvaluationService> logger) : IPredictionEvaluationService
    {
        // Starting threshold, not load-bearing (flagged as tunable in TASK-013) — matches the same
        // 3-way granularity StockTrendAnalysis's own bullish/bearish/neutral field already uses.
        private const decimal NeutralBandPercent = 0.03m;

        // How far on either side of a horizon's target date to fetch/accept a price — covers
        // weekends/holidays around the target trading day. IMarketDataProvider.FetchAsync's fixed
        // ~6-month trailing-from-now window can't be reused here: a due long-horizon (12mo)
        // prediction's target date is itself up to ~3 years in the past, well outside that window.
        // So this fetches (and only accepts a match from) a small range centered on the actual
        // target date instead — see TASK-013's own notes pointing at FetchRangeAsync for exactly
        // this reason.
        private const int RangeWindowDays = 10;

        private readonly IDbContextFactory<AzureSQLDBContext> _dbContextFactory = dbContextFactory;
        private readonly IMarketDataProviderFactory _providerFactory = providerFactory;
        private readonly ITWStockLibHistoricalProvider _twHistoricalProvider = twHistoricalProvider;
        private readonly YahooFinanceMarketDataProvider _yahooProvider = yahooProvider;
        private readonly ILogger<PredictionEvaluationService> _logger = logger;

        public async Task EvaluateDuePredictionsAsync(int batchSize = 5, CancellationToken cancellationToken = default)
        {
            // Own short-lived context — this runs fire-and-forget, potentially after the request
            // scope that triggered it has already been disposed, so it can't share a scoped
            // DbContext with anything else (same reasoning as PersistentTwHistoricalProvider).
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var due = await db.StockPredictions
                .Where(p => p.EvaluatedAtUtc == null
                    && p.PriceAtPrediction != null
                    && p.PredictedAtUtc.AddMonths(p.HorizonMonthsMin) <= now)
                .OrderBy(p => p.PredictedAtUtc)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (due.Count == 0) return;

            foreach (var prediction in due)
            {
                await EvaluateOneAsync(prediction, now, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Evaluates a single row in place (mutating its evaluation fields on success). Never
        /// throws — a lookup failure is logged and the row is left with a null
        /// <see cref="StockPrediction.EvaluatedAtUtc"/> so it's retried on the next pass, rather than
        /// aborting the rest of the batch.
        /// </summary>
        private async Task EvaluateOneAsync(StockPrediction prediction, DateTime now, CancellationToken cancellationToken)
        {
            try
            {
                if (!Enum.TryParse<MarketRegion>(prediction.Market, ignoreCase: true, out var region))
                    region = _providerFactory.DetectRegion(prediction.StockCode);

                var targetDate = prediction.PredictedAtUtc.AddMonths(prediction.HorizonMonthsMin);
                var windowStart = targetDate.AddDays(-RangeWindowDays);
                var windowEnd = targetDate.AddDays(RangeWindowDays);
                if (windowEnd > now) windowEnd = now;

                var marketData = region == MarketRegion.US
                    ? await _yahooProvider.FetchRangeAsync(prediction.StockCode, windowStart, windowEnd, cancellationToken)
                    : await _twHistoricalProvider.FetchRangeAsync(
                        prediction.StockCode, windowStart, windowEnd, knownMarketType: null, cancellationToken);

                var nearest = NearestPrice(marketData.HistoricalPrices, targetDate, RangeWindowDays);
                if (nearest is null)
                {
                    _logger.LogWarning(
                        "No historical price within {window}d of {targetDate:yyyy-MM-dd} for {stockCode} ({market}); leaving prediction {id} unevaluated",
                        RangeWindowDays, targetDate, prediction.StockCode, prediction.Market, prediction.Id);
                    return;
                }

                var baseline = prediction.PriceAtPrediction!.Value;
                var change = baseline == 0 ? 0 : (nearest.Close - baseline) / baseline;
                var actualTrend = change > NeutralBandPercent ? "bullish"
                    : change < -NeutralBandPercent ? "bearish"
                    : "neutral";

                prediction.ActualTrend = actualTrend;
                prediction.ActualPriceAtEvaluation = nearest.Close;
                prediction.WasCorrect = actualTrend == prediction.PredictedTrend;
                prediction.EvaluatedAtUtc = now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to evaluate prediction {id} for {stockCode} ({market}); leaving unevaluated for the next pass",
                    prediction.Id, prediction.StockCode, prediction.Market);
            }
        }

        /// <summary>The historical price whose date is closest to <paramref name="targetDate"/>,
        /// or null if none is within <paramref name="maxGapDays"/> of it (including an empty fetch)
        /// — a lookup that can't find anything reasonably close must be treated as a failure, not
        /// silently graded against whatever happened to be fetched.</summary>
        private static DailyPrice? NearestPrice(List<DailyPrice> prices, DateTime targetDate, int maxGapDays)
        {
            DailyPrice? nearest = null;
            var smallestGap = TimeSpan.MaxValue;
            foreach (var price in prices)
            {
                var gap = (price.Date - targetDate).Duration();
                if (gap < smallestGap)
                {
                    smallestGap = gap;
                    nearest = price;
                }
            }
            return smallestGap <= TimeSpan.FromDays(maxGapDays) ? nearest : null;
        }
    }
}
