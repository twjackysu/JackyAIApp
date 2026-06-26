using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.OpenAI;
using JackyAIApp.Server.Services.Prompt;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for financial analysis using OpenAI: per-stock trend analysis (structured JSON)
    /// and the daily market summary (chunked chat completions).
    /// </summary>
    public class FinanceAnalysisService : IFinanceAnalysisService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IPromptLoader _promptLoader;
        private readonly ILogger<FinanceAnalysisService> _logger;
        private readonly IOpenAIPromptService _openAIPromptService;
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int MAX_ITEMS_PER_CHUNK = 30; // Adjustable chunk size

        /// <summary>
        /// Model used for per-stock AI trend analysis. Swap to <see cref="NewModels.GPT_5_NANO"/>
        /// for a stronger (but pricier) analyst. GPT_4_1_NANO is currently the cheapest.
        /// </summary>
        private const string TrendAnalysisModel = NewModels.GPT_4_1_NANO;

        public FinanceAnalysisService(
            IOpenAIService openAIService,
            IPromptLoader promptLoader,
            ILogger<FinanceAnalysisService> logger,
            IOpenAIPromptService openAIPromptService)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAIPromptService = openAIPromptService ?? throw new ArgumentNullException(nameof(openAIPromptService));
        }

        /// <summary>
        /// Analyzes stock data with OpenAI to generate a fixed-format trend prediction.
        /// The system prompt (StockAnalysisSystem.txt) constrains the model to return the exact
        /// <see cref="StockTrendAnalysis"/> JSON shape, so no chat/streaming is involved.
        /// </summary>
        public async Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default)
        {
            // The system prompt references the data sections (technical/chip/fundamental by
            // timeframe) that GetStockDataAsync produced; pass them as the user message.
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
                    return (analysis, null);
                }

                lastError = error ?? "OpenAI returned no analysis";
                _logger.LogWarning("Stock analysis attempt {attempt}/{max} failed for {stockCode}: {error}",
                    attempt, MAX_RETRY_ATTEMPTS, stockCodeOrName, lastError);

                if (attempt < MAX_RETRY_ATTEMPTS)
                    await Task.Delay(1000 * attempt, cancellationToken);
            }

            return (null, lastError ?? "Analysis failed after all retry attempts");
        }

        /// <summary>
        /// Analyzes Taiwan stock news data using Chat API with chunked processing.
        /// </summary>
        public async Task<List<StrategicInsight>?> AnalyzeWithChatAPIAsync(string rawData, CancellationToken cancellationToken = default)
        {
            try
            {
                // Split data into safe chunks
                var chunks = SplitJsonArraySafely(rawData, MAX_ITEMS_PER_CHUNK);
                var allInsights = new List<StrategicInsight>();

                var systemPrompt = _promptLoader.GetPrompt("Prompt/Finance/DailyImportantInfoSystem.txt");
                if (string.IsNullOrEmpty(systemPrompt))
                {
                    _logger.LogError("Failed to load DailyImportantInfoSystem.txt prompt");
                    return null;
                }

                // Process each chunk
                foreach (var chunk in chunks)
                {
                    var chunkInsights = await ProcessChunkWithRetry(systemPrompt, chunk, cancellationToken);
                    if (chunkInsights != null)
                    {
                        allInsights.AddRange(chunkInsights);
                    }
                }

                return allInsights.Count > 0 ? allInsights : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze data with Chat API");
                return null;
            }
        }

        /// <summary>
        /// Safely splits JSON array into chunks without breaking JSON structure.
        /// </summary>
        private List<string> SplitJsonArraySafely(string jsonArray, int maxItemsPerChunk)
        {
            try
            {
                var items = JsonConvert.DeserializeObject<JArray>(jsonArray);
                var batches = new List<string>();

                if (items == null || items.Count == 0)
                {
                    return batches;
                }

                for (int i = 0; i < items.Count; i += maxItemsPerChunk)
                {
                    var batch = items.Skip(i).Take(maxItemsPerChunk);
                    var batchJson = JsonConvert.SerializeObject(batch, Formatting.None);
                    batches.Add(batchJson);
                }

                _logger.LogInformation("Split {totalItems} items into {batchCount} chunks", items.Count, batches.Count);
                return batches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to split JSON array safely");
                return new List<string> { jsonArray }; // Fallback to original data
            }
        }

        /// <summary>
        /// Processes a single chunk with retry mechanism.
        /// </summary>
        private async Task<List<StrategicInsight>?> ProcessChunkWithRetry(string systemPrompt, string chunkData, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var completionRequest = new ChatCompletionCreateRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.FromSystem(systemPrompt),
                            ChatMessage.FromUser($"請分析以下台股重大訊息資料：\n\n{chunkData}")
                        },
                        Model = Models.Gpt_4o_mini,
                        Temperature = 0.3f,
                        MaxTokens = 4000
                    };

                    var completionResponse = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);

                    if (!completionResponse.Successful)
                    {
                        _logger.LogWarning("Chat API failed (Attempt {attempt}/{max}): {error}",
                            attempt, MAX_RETRY_ATTEMPTS, completionResponse.Error?.Message);
                        if (attempt == MAX_RETRY_ATTEMPTS) return null;
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    var content = completionResponse.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                    content = content.Replace("```json", "").Replace("```", "").Trim();

                    if (string.IsNullOrEmpty(content))
                    {
                        _logger.LogWarning("Chat API returned empty content (Attempt {attempt}/{max})", attempt, MAX_RETRY_ATTEMPTS);
                        if (attempt == MAX_RETRY_ATTEMPTS) return null;
                        continue;
                    }

                    // Handle both direct array format and wrapped object format
                    try
                    {
                        // Try direct array format first
                        return JsonConvert.DeserializeObject<List<StrategicInsight>>(content);
                    }
                    catch (Newtonsoft.Json.JsonException)
                    {
                        // If that fails, try wrapped object format
                        var wrappedResult = JsonConvert.DeserializeObject<dynamic>(content);
                        if (wrappedResult?.result != null)
                        {
                            var resultArray = wrappedResult.result.ToString();
                            return JsonConvert.DeserializeObject<List<StrategicInsight>>(resultArray);
                        }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Chunk processing attempt {attempt} failed", attempt);
                    if (attempt == MAX_RETRY_ATTEMPTS) return null;
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return null;
        }
    }
}