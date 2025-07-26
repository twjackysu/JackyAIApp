using JackyAIApp.Server.DTO;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Interface for financial analysis service using OpenAI.
    /// </summary>
    public interface IFinanceAnalysisService
    {
        /// <summary>
        /// Analyzes stock data using OpenAI to generate trend predictions.
        /// </summary>
        /// <param name="stockCodeOrName">Stock identifier.</param>
        /// <param name="stockData">Raw stock data from TWSE APIs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Stock trend analysis result with detailed error information.</returns>
        Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs financial analysis with timeout and retry mechanism for daily market insights.
        /// </summary>
        /// <param name="fileId">File ID for analysis.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Analysis results or null if failed.</returns>
        Task<List<StrategicInsight>?> RunFinancialAnalysisAsync(string fileId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Analyzes Taiwan stock news data using Chat API with chunked processing.
        /// </summary>
        /// <param name="rawData">Raw JSON data from Taiwan Stock Exchange API.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Analysis results or null if failed.</returns>
        Task<List<StrategicInsight>?> AnalyzeWithChatAPIAsync(string rawData, CancellationToken cancellationToken = default);

        /// <summary>
        /// Processes the analysis result from OpenAI messages.
        /// </summary>
        /// <param name="threadId">Thread ID to retrieve messages from.</param>
        /// <returns>Parsed strategic insights or null if failed.</returns>
        Task<List<StrategicInsight>?> ProcessAnalysisResultAsync(string threadId);
    }
}