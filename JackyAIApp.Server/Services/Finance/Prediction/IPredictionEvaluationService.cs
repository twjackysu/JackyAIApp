namespace JackyAIApp.Server.Services.Finance.Prediction
{
    /// <summary>
    /// Opportunistically grades due, unevaluated <c>StockPrediction</c> rows against actual price
    /// movement. Deliberately not a scheduled/background job — this codebase has no recurring-job
    /// infrastructure (see ARCHITECTURE.md); instead this is called as a small, capped,
    /// fire-and-forget batch from an already-frequent endpoint (`market-digest`), so evaluation
    /// happens roughly daily in aggregate across all users with no new moving parts.
    /// </summary>
    public interface IPredictionEvaluationService
    {
        /// <summary>
        /// Evaluates up to <paramref name="batchSize"/> due predictions (oldest-due-first). Never
        /// throws — a lookup failure for one row is logged and that row is left unevaluated for the
        /// next pass; failures never abandon the rest of the batch.
        /// </summary>
        Task EvaluateDuePredictionsAsync(int batchSize = 5, CancellationToken cancellationToken = default);
    }
}
