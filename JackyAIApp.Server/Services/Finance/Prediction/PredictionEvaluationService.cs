using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Services.Finance.Prediction
{
    public class PredictionEvaluationService(
        IDbContextFactory<AzureSQLDBContext> dbContextFactory,
        IMarketDataProviderFactory providerFactory,
        ILogger<PredictionEvaluationService> logger) : IPredictionEvaluationService
    {
        // Starting threshold, not load-bearing (flagged as tunable in TASK-013) — matches the same
        // 3-way granularity StockTrendAnalysis's own bullish/bearish/neutral field already uses.
        private const decimal NeutralBandPercent = 0.03m;

        private readonly IDbContextFactory<AzureSQLDBContext> _dbContextFactory = dbContextFactory;
        private readonly IMarketDataProviderFactory _providerFactory = providerFactory;
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

                var provider = _providerFactory.GetMarketDataProvider(region);
                var marketData = await provider.FetchAsync(prediction.StockCode, cancellationToken);

                var targetDate = prediction.PredictedAtUtc.AddMonths(prediction.HorizonMonthsMin);
                var nearest = NearestPrice(marketData.HistoricalPrices, targetDate);
                if (nearest is null)
                {
                    _logger.LogWarning(
                        "No historical price available near {targetDate:yyyy-MM-dd} for {stockCode} ({market}); leaving prediction {id} unevaluated",
                        targetDate, prediction.StockCode, prediction.Market, prediction.Id);
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

        /// <summary>The historical price whose date is closest to <paramref name="targetDate"/>, or
        /// null if the fetch returned no history at all.</summary>
        private static DailyPrice? NearestPrice(List<DailyPrice> prices, DateTime targetDate)
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
            return nearest;
        }
    }
}
