using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services.Prompt;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.Json;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for financial analysis using OpenAI and Dify.
    /// </summary>
    public class FinanceAnalysisService : IFinanceAnalysisService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IPromptLoader _promptLoader;
        private readonly ILogger<FinanceAnalysisService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<Settings> _settings;
        private const string ASSISTANT_ID = "asst_5vCsMPtNXvVfsbptyZakpr2m";
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int MAX_ITEMS_PER_CHUNK = 30; // Adjustable chunk size
        private const string DIFY_API_URL = "https://api.dify.ai/v1/chat-messages";

        public FinanceAnalysisService(
            IOpenAIService openAIService,
            IPromptLoader promptLoader,
            ILogger<FinanceAnalysisService> logger,
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<Settings> settings)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _promptLoader = promptLoader ?? throw new ArgumentNullException(nameof(promptLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Analyzes stock data using Dify AI to generate trend predictions.
        /// </summary>
        public async Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, string userId, CancellationToken cancellationToken = default)
        {
            var difyApiKey = _settings.CurrentValue.DifyApiKey2;
            if (string.IsNullOrEmpty(difyApiKey))
            {
                return (null, "DifyApiKey2 is not configured in settings");
            }

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient();

                    var request = new DifyChatRequest
                    {
                        Inputs = new Dictionary<string, string>
                        {
                            { "company_context", stockData }
                        },
                        Query = stockCodeOrName,
                        ResponseMode = "blocking",
                        ConversationId = "",
                        User = userId,
                        Files = new List<object>()
                    };

                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(request);
                    var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, DIFY_API_URL)
                    {
                        Content = httpContent
                    };
                    httpRequestMessage.Headers.Add("Authorization", $"Bearer {difyApiKey}");

                    var response = await httpClient.SendAsync(httpRequestMessage, cancellationToken);
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorDetail = $"Dify API failed (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}): Status {response.StatusCode}, Response: {responseContent}";
                        _logger.LogError("Dify API call failed for stock {stockCode}: {error}", stockCodeOrName, errorDetail);
                        if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            return (null, errorDetail);
                        }
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    var difyResponse = System.Text.Json.JsonSerializer.Deserialize<DifyChatResponse>(responseContent);
                    var content = difyResponse?.Answer ?? "";
                    content = content.Replace("```json", "").Replace("```", "").Trim();

                    if (string.IsNullOrEmpty(content))
                    {
                        var errorDetail = $"Dify API returned empty content (Attempt {attempt}/{MAX_RETRY_ATTEMPTS})";
                        if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            return (null, errorDetail);
                        }
                        continue;
                    }

                    try
                    {
                        var analysis = JsonConvert.DeserializeObject<StockTrendAnalysis>(content);
                        if (analysis != null)
                        {
                            analysis.LastUpdated = DateTime.UtcNow;
                            _logger.LogInformation("Successfully analyzed stock {stockCode} using Dify API", stockCodeOrName);
                            return (analysis, null);
                        }
                        else
                        {
                            var errorDetail = $"Failed to deserialize Dify response to StockTrendAnalysis (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}). Content: {content.Substring(0, Math.Min(content.Length, 200))}...";
                            if (attempt == MAX_RETRY_ATTEMPTS)
                            {
                                return (null, errorDetail);
                            }
                        }
                    }
                    catch (Newtonsoft.Json.JsonException jsonEx)
                    {
                        var errorDetail = $"JSON deserialization failed (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}): {jsonEx.Message}. Content: {content.Substring(0, Math.Min(content.Length, 200))}...";
                        if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            return (null, errorDetail);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var errorDetail = $"Unexpected error (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}): {ex.Message}. Stack trace: {ex.StackTrace}";
                    _logger.LogWarning(ex, "Stock analysis attempt {attempt} failed for {stockCode}", attempt, stockCodeOrName);
                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        return (null, errorDetail);
                    }
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return (null, "Analysis failed after all retry attempts");
        }

        /// <summary>
        /// Runs financial analysis with timeout and retry mechanism.
        /// </summary>
        public async Task<List<StrategicInsight>?> RunFinancialAnalysisAsync(string fileId, CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var createThreadAndRunResponse = await _openAIService.Beta.Runs.CreateThreadAndRun(new CreateThreadAndRunRequest
                    {
                        AssistantId = ASSISTANT_ID,
                        Thread = new ThreadCreateRequest
                        {
                            Messages = new List<MessageCreateRequest>
                            {
                                new MessageCreateRequest
                                {
                                    Role = "user",
                                    Content = new MessageContentOneOfType(new List<MessageContent>
                                    {
                                        MessageContent.TextContent("Analyze today's major news about listed companies and provide strategic insights for all companies with significant investment implications.")
                                    }),
                                    Attachments = new List<Attachment>
                                    {
                                        new Attachment
                                        {
                                            FileId = fileId,
                                            Tools = new List<ToolDefinition>
                                            {
                                                new ToolDefinition { Type = "file_search" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    });

                    if (!createThreadAndRunResponse.Successful)
                    {
                        if (attempt == MAX_RETRY_ATTEMPTS) return null;
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    string threadId = createThreadAndRunResponse.ThreadId;
                    string runId = createThreadAndRunResponse.Id;

                    // Monitor run status with timeout
                    var maxAnalysisTime = TimeSpan.FromMinutes(5);
                    var startTime = DateTime.UtcNow;
                    var delay = TimeSpan.FromSeconds(3);
                    var maxDelay = TimeSpan.FromSeconds(15);

                    while (DateTime.UtcNow - startTime < maxAnalysisTime)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await Task.Delay(delay, cancellationToken);

                        var runStatusResponse = await _openAIService.Beta.Runs.RunRetrieve(threadId, runId);

                        if (runStatusResponse.Status == "completed")
                        {
                            return await ProcessAnalysisResultAsync(threadId);
                        }

                        if (runStatusResponse.Status == "failed" || runStatusResponse.Status == "cancelled")
                        {
                            _logger.LogWarning("Analysis run failed with status: {status}", runStatusResponse.Status);
                            break;
                        }

                        // Exponential backoff
                        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.2, maxDelay.TotalMilliseconds));
                    }

                    if (attempt == MAX_RETRY_ATTEMPTS)
                    {
                        _logger.LogError("Analysis timed out after {minutes} minutes", maxAnalysisTime.TotalMinutes);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analysis attempt {attempt} failed", attempt);
                    if (attempt == MAX_RETRY_ATTEMPTS) return null;
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return null;
        }

        /// <summary>
        /// Processes the analysis result from OpenAI messages.
        /// </summary>
        public async Task<List<StrategicInsight>?> ProcessAnalysisResultAsync(string threadId)
        {
            try
            {
                var getMessageListResponse = await _openAIService.Beta.Messages.ListMessages(threadId);

                if (!getMessageListResponse.Successful)
                {
                    return null;
                }

                _logger.LogInformation("Generate financial analysis result: {json}",
                    JsonConvert.SerializeObject(getMessageListResponse.Data, Formatting.Indented));

                var content = getMessageListResponse.Data?.FirstOrDefault()?.Content?.FirstOrDefault()?.Text?.Value ?? "";
                content = content.Replace("```json", "").Replace("```", "").Trim();

                if (string.IsNullOrEmpty(content))
                {
                    return null;
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
                _logger.LogError(ex, "Failed to process analysis result");
                return null;
            }
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