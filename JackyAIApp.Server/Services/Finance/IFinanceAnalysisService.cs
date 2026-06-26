using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Interface for financial analysis service using OpenAI.
    /// </summary>
    public interface IFinanceAnalysisService
    {
        /// <summary>
        /// Analyzes stock data with OpenAI to generate a fixed-format trend prediction.
        /// </summary>
        /// <param name="stockCodeOrName">Stock identifier.</param>
        /// <param name="stockData">Raw stock data from TWSE APIs.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Stock trend analysis result with detailed error information.</returns>
        Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes Taiwan stock news data using Chat API with chunked processing.
        /// </summary>
        /// <param name="rawData">Raw JSON data from Taiwan Stock Exchange API.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Analysis results or null if failed.</returns>
        Task<List<StrategicInsight>?> AnalyzeWithChatAPIAsync(string rawData, CancellationToken cancellationToken = default);
    }
}