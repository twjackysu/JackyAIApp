using Betalgo.Ranul.OpenAI.ObjectModels;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;

namespace JackyAIApp.Server.Services.Finance
{
    public class FinanceAnalysisService : IFinanceAnalysisService
    {
        private readonly ILogger<FinanceAnalysisService> _logger;
        private readonly IOpenAIPromptService _openAIPromptService;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const string TrendAnalysisModel = NewModels.GPT_6_LUNA;

        public FinanceAnalysisService(
            ILogger<FinanceAnalysisService> logger,
            IOpenAIPromptService openAIPromptService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAIPromptService = openAIPromptService ?? throw new ArgumentNullException(nameof(openAIPromptService));
        }

        /// <summary>
        /// Analyzes stock data with OpenAI to generate a fixed-format trend prediction.
        /// The system prompt (StockAnalysisSystem.txt) constrains the model to return the exact
        /// <see cref="StockTrendAnalysis"/> JSON shape, so no chat/streaming is involved.
        /// </summary>
        public async Task<ServiceResult<StockTrendAnalysis>> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default)
        {
            // The system prompt references the data sections (technical/chip/fundamental by
            // timeframe) that TwseAnalysisData.DescribeAsync produced; pass them as the user message.
            var userMessage = $"請分析股票「{stockCodeOrName}」，以下是依時間維度整理的資料：\n\n{stockData}";

            string? lastError = null;
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                var (analysis, error) = await _openAIPromptService.GetCompletionAsync<StockTrendAnalysis>(
                    "Prompt/Finance/StockAnalysisSystem.txt",
                    userMessage,
                    TrendAnalysisModel);

                if (analysis != null)
                {
                    analysis.LastUpdated = DateTime.UtcNow;
                    _logger.LogInformation("Successfully analyzed stock {stockCode} using OpenAI ({model})",
                        stockCodeOrName, TrendAnalysisModel);
                    return ServiceResult.Ok(analysis);
                }

                lastError = error ?? "OpenAI returned no analysis";
                _logger.LogWarning("Stock analysis attempt {attempt}/{max} failed for {stockCode}: {error}",
                    attempt, MAX_RETRY_ATTEMPTS, stockCodeOrName, lastError);

                if (attempt < MAX_RETRY_ATTEMPTS)
                    await Task.Delay(1000 * attempt, cancellationToken);
            }

            return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, lastError ?? "Analysis failed after all retry attempts");
        }

    }
}