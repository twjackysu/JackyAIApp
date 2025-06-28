using Betalgo.Ranul.OpenAI.Interfaces;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace JackyAIApp.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinanceController(
        ILogger<FinanceController> logger,
        IOptionsMonitor<Settings> settings,
        IMyResponseFactory responseFactory,
        AzureSQLDBContext DBContext,
        IUserService userService,
        IOpenAIService openAIService,
        IHttpClientFactory httpClientFactory,
        IExtendedMemoryCache memoryCache,
        ITWSEOpenAPIService twseApiService) : ControllerBase
    {
        private readonly ILogger<FinanceController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly IOpenAIService _openAIService = openAIService;
        private readonly HttpClient _httpClient = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly ITWSEOpenAPIService _twseApiService = twseApiService ?? throw new ArgumentNullException(nameof(twseApiService));
        
        // Cache keys and constants
        private const string VECTOR_STORE_ID = "vs_681efca3b2388191a761e64f1f7250ac";
        private const string ASSISTANT_ID = "asst_5vCsMPtNXvVfsbptyZakpr2m";
        private const int MAX_RETRY_ATTEMPTS = 3;
        private const int OPERATION_TIMEOUT_SECONDS = 300; // 5 minutes
        
        // Static cache for file IDs to avoid repeated uploads across requests
        private static readonly ConcurrentDictionary<string, string> _fileIdCache = new();

        /// <summary>
        /// Gets the daily important financial information from Taiwan Stock Exchange.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for request timeout control.</param>
        /// <returns>An IActionResult containing the strategic insights or an error response.</returns>
        [HttpGet("dailyimportantinfo")]
        public async Task<IActionResult> GetDailyImportantInfo(CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));
            
            try
            {
                // Step 1: Fetch raw material information from Taiwan Stock Exchange API with caching
                string rawMaterialInfo = await GetRawMaterialInfoWithCacheAsync(timeoutCts.Token);
                if (string.IsNullOrEmpty(rawMaterialInfo))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch data from Taiwan Stock Exchange API.");
                }

                var date = rawMaterialInfo.Substring(11, 7);
                var fileName = $"{date}.json";
                var resultCacheKey = $"{nameof(GetDailyImportantInfo)}_{fileName}";

                // Step 2: Check if the final result is cached
                if (_memoryCache.TryGetValue(resultCacheKey, out List<StrategicInsight>? result))
                {
                    return _responseFactory.CreateOKResponse(result);
                }

                // Step 3: Get or upload file with optimized file management
                string fileId = await GetOrUploadFileWithRetryAsync(rawMaterialInfo, fileName, timeoutCts.Token);
                if (string.IsNullOrEmpty(fileId))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to manage file in OpenAI.");
                }

                // Step 4: Ensure vector store is ready with optimized operations
                bool vectorStoreReady = await EnsureVectorStoreReadyAsync(fileId, timeoutCts.Token);
                if (!vectorStoreReady)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to prepare vector store.");
                }

                // Step 5: Create thread and run analysis with timeout
                var analysisResult = await RunFinancialAnalysisAsync(fileId, timeoutCts.Token);
                if (analysisResult == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, "Analysis failed or timed out.");
                }

                // Step 6: Cache and return result
                _memoryCache.Set(resultCacheKey, analysisResult, TimeSpan.FromDays(0.5));
                return _responseFactory.CreateOKResponse(analysisResult);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Operation timed out after {seconds} seconds", OPERATION_TIMEOUT_SECONDS);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Operation timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting daily important information.");
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Internal server error occurred.");
            }
        }

        /// <summary>
        /// Fetches raw material information from Taiwan Stock Exchange API with caching.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>The API response as a string.</returns>
        private async Task<string> GetRawMaterialInfoWithCacheAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.Today.ToString("yyyyMMdd");
            var apiCacheKey = $"twse_raw_data_{today}";
            
            // Check cache first
            if (_memoryCache.TryGetValue(apiCacheKey, out string? cachedData) && !string.IsNullOrEmpty(cachedData))
            {
                return cachedData;
            }

            try
            {
                string apiUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap04_L";

                using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    
                    // Cache for 1 hour to avoid frequent API calls
                    _memoryCache.Set(apiCacheKey, jsonContent, TimeSpan.FromHours(1));
                    
                    return jsonContent;
                }
                else
                {
                    _logger.LogError("Failed to fetch data from Taiwan Stock Exchange API. Status code: {StatusCode}", response.StatusCode);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching data from Taiwan Stock Exchange API.");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets or uploads file to OpenAI with retry mechanism and caching.
        /// </summary>
        /// <param name="content">File content to upload.</param>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>File ID from OpenAI.</returns>
        private async Task<string> GetOrUploadFileWithRetryAsync(string content, string fileName, CancellationToken cancellationToken)
        {
            // Check static cache first
            if (_fileIdCache.TryGetValue(fileName, out string? cachedFileId))
            {
                // We'll verify file existence during the list operation below
                // For now, trust the cache but verify during the main flow
                return cachedFileId;
            }

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    // List and clean old files
                    var listFilesResponse = await _openAIService.Files.ListFile();
                    if (!listFilesResponse.Successful)
                    {
                        if (attempt == MAX_RETRY_ATTEMPTS) return string.Empty;
                        await Task.Delay(1000 * attempt, cancellationToken);
                        continue;
                    }

                    string? fileId = null;
                    var filesToDelete = new List<string>();

                    // Batch identify files to delete and find existing file
                    foreach (var file in listFilesResponse?.Data ?? [])
                    {
                        if (file.FileName == fileName)
                        {
                            fileId = file.Id;
                            // Update cache with verified file ID
                            _fileIdCache.TryAdd(fileName, fileId);
                        }
                        else if (file.FileName.EndsWith(".json"))
                        {
                            filesToDelete.Add(file.Id);
                        }
                    }

                    // If we had a cached file ID but it wasn't found in the list, remove it from cache
                    if (_fileIdCache.TryGetValue(fileName, out string? oldCachedId) && fileId != oldCachedId)
                    {
                        _fileIdCache.TryRemove(fileName, out _);
                        fileId = null;
                    }

                    // Batch delete old files (fire and forget for performance)
                    _ = Task.Run(async () =>
                    {
                        foreach (var fileIdToDelete in filesToDelete)
                        {
                            try
                            {
                                await _openAIService.Files.DeleteFile(fileIdToDelete);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete old file {fileId}", fileIdToDelete);
                            }
                        }
                    }, cancellationToken);

                    // Upload file if not exists
                    if (fileId == null)
                    {
                        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                        var fileUploadResult = await _openAIService.Files.FileUpload("assistants", stream, fileName);
                        if (fileUploadResult.Successful)
                        {
                            fileId = fileUploadResult.Id;
                        }
                        else if (attempt == MAX_RETRY_ATTEMPTS)
                        {
                            return string.Empty;
                        }
                    }

                    if (!string.IsNullOrEmpty(fileId))
                    {
                        _fileIdCache.TryAdd(fileName, fileId);
                        return fileId;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Attempt {attempt} failed for file upload", attempt);
                    if (attempt == MAX_RETRY_ATTEMPTS) return string.Empty;
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Ensures vector store is ready with optimized polling.
        /// </summary>
        /// <param name="fileId">File ID to add to vector store.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if vector store is ready.</returns>
        private async Task<bool> EnsureVectorStoreReadyAsync(string fileId, CancellationToken cancellationToken)
        {
            try
            {
                // Check if file already exists in vector store
                var listVectorStoreFilesResponse = await _openAIService.Beta.VectorStoreFiles.ListVectorStoreFiles(VECTOR_STORE_ID, new VectorStoreFileListRequest());
                if (!listVectorStoreFilesResponse.Successful)
                {
                    return false;
                }

                bool fileExistsInVectorStore = false;
                var filesToDelete = new List<string>();

                foreach (var vsFile in listVectorStoreFilesResponse?.Data ?? [])
                {
                    if (vsFile.Id == fileId)
                    {
                        fileExistsInVectorStore = true;
                    }
                    else
                    {
                        filesToDelete.Add(vsFile.Id);
                    }
                }

                // Clean old vector store files (fire and forget)
                _ = Task.Run(async () =>
                {
                    foreach (var fileIdToDelete in filesToDelete)
                    {
                        try
                        {
                            await _openAIService.Beta.VectorStoreFiles.DeleteVectorStoreFile(VECTOR_STORE_ID, fileIdToDelete);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete vector store file {fileId}", fileIdToDelete);
                        }
                    }
                }, cancellationToken);

                if (fileExistsInVectorStore)
                {
                    return true;
                }

                // Create vector store file
                var createVSFilesResponse = await _openAIService.Beta.VectorStoreFiles.CreateVectorStoreFile(VECTOR_STORE_ID, new CreateVectorStoreFileRequest { FileId = fileId });
                if (!createVSFilesResponse.Successful)
                {
                    return false;
                }

                // Wait for processing with exponential backoff and timeout
                var maxWaitTime = TimeSpan.FromMinutes(3);
                var startTime = DateTime.UtcNow;
                var delay = TimeSpan.FromSeconds(2);
                var maxDelay = TimeSpan.FromSeconds(10);

                while (DateTime.UtcNow - startTime < maxWaitTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    await Task.Delay(delay, cancellationToken);
                    
                    var getVSResponse = await _openAIService.Beta.VectorStores.RetrieveVectorStore(VECTOR_STORE_ID);
                    
                    if (!getVSResponse.Successful)
                    {
                        return false;
                    }
                    
                    if (getVSResponse.FileCounts.Failed > 0)
                    {
                        _logger.LogError("Vector store processing failed for file {fileId}", fileId);
                        return false;
                    }
                    
                    if (getVSResponse.FileCounts.Completed > 0)
                    {
                        return true;
                    }
                    
                    // Exponential backoff
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, maxDelay.TotalMilliseconds));
                }

                _logger.LogWarning("Vector store processing timed out for file {fileId}", fileId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring vector store readiness");
                return false;
            }
        }

        /// <summary>
        /// Runs financial analysis with timeout and retry mechanism.
        /// </summary>
        /// <param name="fileId">File ID for analysis.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Analysis results or null if failed.</returns>
        private async Task<List<StrategicInsight>?> RunFinancialAnalysisAsync(string fileId, CancellationToken cancellationToken)
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
                                        MessageContent.TextContent("From today's major news about listed companies, select five to ten companies with the greatest growth potential and one to five companies that may decline, and explain the reasons. So you need to list at least 6 companies (5 growing and 1 declining) and at most 15 companies (10 growing and 5 declining).")
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
        /// <param name="threadId">Thread ID to retrieve messages from.</param>
        /// <returns>Parsed strategic insights or null if failed.</returns>
        private async Task<List<StrategicInsight>?> ProcessAnalysisResultAsync(string threadId)
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

                return JsonConvert.DeserializeObject<List<StrategicInsight>>(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process analysis result");
                return null;
            }
        }

        /// <summary>
        /// Analyzes a specific stock using MCP Server and OpenAI.
        /// </summary>
        /// <param name="request">Stock search request containing stock code or company name.</param>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>Stock trend analysis with short, medium, and long-term predictions.</returns>
        [HttpPost("analyze-stock")]
        public async Task<IActionResult> AnalyzeStock([FromBody] StockSearchRequest request, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));

            try
            {
                if (string.IsNullOrWhiteSpace(request.StockCodeOrName))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Stock code or name is required.");
                }

                var cacheKey = $"stock_analysis_{request.StockCodeOrName.ToUpper()}_{DateTime.Today:yyyyMMdd}";
                
                // Check cache first
                if (_memoryCache.TryGetValue(cacheKey, out StockTrendAnalysis? cachedResult))
                {
                    return _responseFactory.CreateOKResponse(cachedResult);
                }

                // First, resolve user input to a valid stock code
                var resolvedStockCode = await _twseApiService.ResolveStockCodeAsync(request.StockCodeOrName, timeoutCts.Token);
                if (string.IsNullOrEmpty(resolvedStockCode))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Unable to find stock code for '{request.StockCodeOrName}'. Please verify the company name or stock code. Searched in TWSE company database but found no matches.");
                }

                // Call TWSE Open API service to get stock data using resolved stock code
                var stockData = await _twseApiService.GetStockDataAsync(resolvedStockCode, timeoutCts.Token);
                if (string.IsNullOrEmpty(stockData))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Failed to retrieve stock data from TWSE APIs for stock code '{resolvedStockCode}'. TWSE API endpoints may be unavailable or returned empty data.");
                }

                // Analyze the stock data using OpenAI with the resolved stock code
                var (analysis, errorDetail) = await AnalyzeStockWithAIAsync(resolvedStockCode, stockData, timeoutCts.Token);
                if (analysis == null)
                {
                    var detailedError = $"Failed to analyze stock data for '{resolvedStockCode}'. Details: {errorDetail ?? "Unknown error"}";
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, detailedError);
                }

                // Cache the result for 6 hours
                if(analysis != null)
                {
                    _memoryCache.Set(cacheKey, analysis, TimeSpan.FromHours(6));
                }

                return _responseFactory.CreateOKResponse(analysis);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Stock analysis timed out for {stockCode}", request.StockCodeOrName);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, $"Analysis timed out after {OPERATION_TIMEOUT_SECONDS} seconds for stock '{request.StockCodeOrName}'. Please try again later.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while analyzing stock {stockCode}", request.StockCodeOrName);
                var detailedError = $"Unexpected error occurred while analyzing stock '{request.StockCodeOrName}': {ex.Message}";
                if (ex.InnerException != null)
                {
                    detailedError += $" Inner exception: {ex.InnerException.Message}";
                }
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, detailedError);
            }
        }

        /// <summary>
        /// Analyzes stock data using OpenAI to generate trend predictions.
        /// </summary>
        /// <param name="stockCodeOrName">Stock identifier.</param>
        /// <param name="stockData">Raw stock data from MCP Server.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Stock trend analysis result with detailed error information.</returns>
        private async Task<(StockTrendAnalysis? analysis, string? errorDetail)> AnalyzeStockWithAIAsync(string stockCodeOrName, string stockData, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var systemPrompt = await System.IO.File.ReadAllTextAsync("Prompt/Finance/StockAnalysisSystem.txt", cancellationToken);

                    var userMessage = $"請分析以下股票代碼 {stockCodeOrName} 的資料，並提供完整的趨勢預測：\n\n{stockData}";

                    var completionRequest = new ChatCompletionCreateRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.FromSystem(systemPrompt),
                            ChatMessage.FromUser("3679"),
                            ChatMessage.FromAssistant("{\n  \"stockCode\": \"3679\",\n  \"companyName\": \"新至陞科技股份有限公司\",\n  \"currentPrice\": 129.50,\n  \"shortTermTrend\": \"neutral\",\n  \"mediumTermTrend\": \"bullish\",\n  \"longTermTrend\": \"bullish\",\n  \"shortTermSummary\": \"在短期內（1-3個月），新至陞的股價表現相對穩定，近期的收盤價129.50元與月平均價133.10元相近，顯示出市場對該股的需求尚可，但缺乏明顯的上漲動力。成交量為125,950股，顯示出一定的市場活躍度，但仍需觀察市場情緒的變化。\",\n  \"mediumTermSummary\": \"在中期內（3-12個月），隨著公司業績的穩定增長及股利政策的支持，預計新至陞的股價將逐步上升。公司最近的淨利表現良好，且股利收益率達到7.72%，這將吸引更多的投資者進場，進一步推動股價上升。\",\n  \"longTermSummary\": \"在長期內（1-3年），隨著公司在科技領域的持續創新及市場需求的增長，新至陞有潛力實現穩定的增長。若公司能夠持續提升其市場競爭力，並保持良好的財務表現，股價有望在未來幾年內顯著上升，形成長期的牛市趨勢。\",\n  \"keyFactors\": [\n    \"公司穩定的財務表現及良好的股利政策\",\n    \"市場對科技股的需求持續增長\",\n    \"公司在行業內的競爭優勢\"\n  ],\n  \"riskFactors\": [\n    \"市場波動可能影響股價表現\",\n    \"行業競爭加劇可能影響利潤率\",\n    \"全球經濟不確定性可能影響業務增長\"\n  ],\n  \"recommendation\": \"buy\",\n  \"confidenceLevel\": \"medium\",\n  \"lastUpdated\": \"2023-10-01T12:00:00\",\n  \"dataSource\": \"MCP Server + AI 分析\"\n}"),
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
    }
}