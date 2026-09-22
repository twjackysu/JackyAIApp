namespace JackyAIApp.Server.Services.Finance.Prediction
{
    /// <summary>
    /// The 3 fixed horizons <c>StockTrendAnalysis</c> always predicts for (short/medium/long,
    /// matching its own doc comments: 1-3mo / 3-12mo / 1-3yr). Shared by the write path
    /// (<see cref="StockPredictionRecorder"/>) and the evaluation path
    /// (<see cref="PredictionEvaluationService"/>) so the two never drift apart.
    /// </summary>
    public static class StockPredictionHorizons
    {
        public const int ShortMin = 1;
        public const int ShortMax = 3;

        public const int MediumMin = 3;
        public const int MediumMax = 12;

        public const int LongMin = 12;
        public const int LongMax = 36;
    }
}
