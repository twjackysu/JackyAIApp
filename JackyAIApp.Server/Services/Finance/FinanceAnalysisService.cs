using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using JackyAIApp.Server.DTO;
using Newtonsoft.Json;

namespace JackyAIApp.Server.Services.Finance
{
    /// <summary>
    /// Service for financial analysis using OpenAI.
    /// </summary>
    public class FinanceAnalysisService : IFinanceAnalysisService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<FinanceAnalysisService> _logger;
        private const string ASSISTANT_ID = "asst_5vCsMPtNXvVfsbptyZakpr2m";
        private const int MAX_RETRY_ATTEMPTS = 3;

        public FinanceAnalysisService(
            IOpenAIService openAIService,
            ILogger<FinanceAnalysisService> logger)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes stock data using OpenAI to generate trend predictions.
        /// </summary>
        public async Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var systemPrompt = await System.IO.File.ReadAllTextAsync("Prompt/Finance/StockAnalysisSystem.txt", cancellationToken);

                    var userMessage = $"請分析以下股票代碼 {stockCodeOrName} 的資料，並提供完整的趨勢預測：\\n\\n{stockData}";

                    var completionRequest = new ChatCompletionCreateRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.FromSystem(systemPrompt),
                            ChatMessage.FromUser("3679"),
                            ChatMessage.FromAssistant("{\\n  \\\"stockCode\\\": \\\"3679\\\",\\n  \\\"companyName\\\": \\\"新至陞科技股份有限公司\\\",\\n  \\\"currentPrice\\\": 129.50,\\n  \\\"shortTermTrend\\\": \\\"neutral\\\",\\n  \\\"mediumTermTrend\\\": \\\"bullish\\\",\\n  \\\"longTermTrend\\\": \\\"bullish\\\",\\n  \\\"shortTermSummary\\\": \\\"在短期內（1-3個月），新至陞的股價表現相對穩定，近期的收盤價129.50元與月平均價133.10元相近，顯示出市場對該股的需求尚可，但缺乏明顯的上漲動力。成交量為125,950股，顯示出一定的市場活躍度，但仍需觀察市場情緒的變化。\\\",\\n  \\\"mediumTermSummary\\\": \\\"在中期內（3-12個月），隨著公司業績的穩定增長及股利政策的支持，預計新至陞的股價將逐步上升。公司最近的淨利表現良好，且股利收益率達到7.72%，這將吸引更多的投資者進場，進一步推動股價上升。\\\",\\n  \\\"longTermSummary\\\": \\\"在長期內（1-3年），隨著公司在科技領域的持續創新及市場需求的增長，新至陞有潛力實現穩定的增長。若公司能夠持續提升其市場競爭力，並保持良好的財務表現，股價有望在未來幾年內顯著上升，形成長期的牛市趨勢。\\\",\\n  \\\"keyFactors\\\": [\\n    \\\"公司穩定的財務表現及良好的股利政策\\\",\\n    \\\"市場對科技股的需求持續增長\\\",\\n    \\\"公司在行業內的競爭優勢\\\"\\n  ],\\n  \\\"riskFactors\\\": [\\n    \\\"市場波動可能影響股價表現\\\",\\n    \\\"行業競爭加劇可能影響利潤率\\\",\\n    \\\"全球經濟不確定性可能影響業務增長\\\"\\n  ],\\n  \\\"recommendation\\\": \\\"buy\\\",\\n  \\\"confidenceLevel\\\": \\\"medium\\\",\\n  \\\"lastUpdated\\\": \\\"2023-10-01T12:00:00\\\",\\n  \\\"dataSource\\\": \\\"MCP Server + AI 分析\\\"\\n}"),
                            ChatMessage.FromUser(userMessage)
                        },
                        Model = Models.Gpt_4o_mini,
                        Temperature = 0.3f,
                        MaxTokens = 2000
                    };

                    var completionResponse = await _openAIService.ChatCompletion.CreateCompletion(completionRequest);

                    if (!completionResponse.Successful)
                    {
                        var errorDetail = $"OpenAI API failed (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}): {completionResponse.Error?.Message ?? "Unknown error"}";
                        if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            _logger.LogError("OpenAI completion failed for stock {stockCode}: {error}", stockCodeOrName, completionResponse.Error?.Message);
                            return (null, errorDetail);
                        }
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    var content = completionResponse.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                    content = content.Replace("```json", "").Replace("```", "").Trim();

                    if (string.IsNullOrEmpty(content))
                    {
                        var errorDetail = $"OpenAI returned empty content (Attempt {attempt}/{MAX_RETRY_ATTEMPTS})";
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
                            return (analysis, null);
                        }
                        else
                        {
                            var errorDetail = $"Failed to deserialize OpenAI response to StockTrendAnalysis (Attempt {attempt}/{MAX_RETRY_ATTEMPTS}). Content: {content.Substring(0, Math.Min(content.Length, 200))}...";
                            if (attempt == MAX_RETRY_ATTEMPTS)
                            {
                                return (null, errorDetail);
                            }
                        }
                    }
                    catch (JsonException jsonEx)
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
                catch (JsonException)
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
    }
}