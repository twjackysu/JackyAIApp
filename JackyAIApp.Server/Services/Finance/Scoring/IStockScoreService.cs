using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Scoring
{
    /// <summary>
    /// Service for computing composite stock scores from multiple indicator categories.
    /// </summary>
    public interface IStockScoreService
    {
        /// <summary>
        /// Compute a full composite score for a stock.
        /// Fetches all required data, runs all indicators, and produces a weighted score.
        /// </summary>
        /// <param name="stockCode">Resolved stock code</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Comprehensive stock score response</returns>
        Task<StockScoreResponse> ScoreAsync(string stockCode, CancellationToken cancellationToken = default);
    }
}
