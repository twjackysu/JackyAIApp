using JackyAIApp.Server.Data;
using JackyAIApp.Server.Data.Models.SQL;
using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance.Prediction
{
    public class StockPredictionRecorder(
        AzureSQLDBContext dbContext,
        ILogger<StockPredictionRecorder> logger) : IStockPredictionRecorder
    {
        private readonly AzureSQLDBContext _dbContext = dbContext;
        private readonly ILogger<StockPredictionRecorder> _logger = logger;

        public async Task RecordAsync(
            string stockCode, string market, string userId, StockTrendAnalysis analysis,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var predictedAt = DateTime.UtcNow;
                _dbContext.StockPredictions.AddRange(
                    BuildRow(stockCode, market, userId, predictedAt, analysis.CurrentPrice,
                        analysis.ShortTermTrend, StockPredictionHorizons.ShortMin, StockPredictionHorizons.ShortMax),
                    BuildRow(stockCode, market, userId, predictedAt, analysis.CurrentPrice,
                        analysis.MediumTermTrend, StockPredictionHorizons.MediumMin, StockPredictionHorizons.MediumMax),
                    BuildRow(stockCode, market, userId, predictedAt, analysis.CurrentPrice,
                        analysis.LongTermTrend, StockPredictionHorizons.LongMin, StockPredictionHorizons.LongMax));

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Never let a bookkeeping failure surface as an `analyze-stock` failure — the AI
                // analysis itself already succeeded and that's what the caller is waiting on.
                _logger.LogWarning(ex, "Failed to persist stock predictions for {stockCode}", stockCode);
            }
        }

        private static StockPrediction BuildRow(
            string stockCode, string market, string userId, DateTime predictedAt, decimal? currentPrice,
            string predictedTrend, int horizonMonthsMin, int horizonMonthsMax) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                StockCode = stockCode,
                Market = market,
                UserId = userId,
                PredictedAtUtc = predictedAt,
                PriceAtPrediction = currentPrice,
                HorizonMonthsMin = horizonMonthsMin,
                HorizonMonthsMax = horizonMonthsMax,
                PredictedTrend = predictedTrend,
            };
    }
}
