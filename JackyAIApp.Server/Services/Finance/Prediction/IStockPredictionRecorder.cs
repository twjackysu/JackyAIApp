using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance.Prediction
{
    /// <summary>
    /// Persists the 3 per-horizon <c>StockPrediction</c> rows for one successful `analyze-stock`
    /// AI call. Pure write — see <see cref="IPredictionEvaluationService"/> for the read-back/grade
    /// side.
    /// </summary>
    public interface IStockPredictionRecorder
    {
        /// <summary>
        /// Writes one <c>StockPrediction</c> row per horizon (short/medium/long) for
        /// <paramref name="analysis"/>. Never throws — a persistence failure is logged and
        /// swallowed so it can never turn a successful AI analysis into a failed `analyze-stock`
        /// response (this is a pure additive side effect, not part of the request's own contract).
        /// </summary>
        Task RecordAsync(
            string stockCode, string market, string userId, StockTrendAnalysis analysis,
            CancellationToken cancellationToken = default);
    }
}
