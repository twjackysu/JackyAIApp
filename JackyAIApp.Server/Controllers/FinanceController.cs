using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DotnetSdkUtilities.Services;

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
        ITWSEOpenAPIService twseApiService,
        ITWSEDataService twseDataService,
        IFinanceAnalysisService financeAnalysisService,
        IExtendedMemoryCache memoryCache) : ControllerBase
    {
        private readonly ILogger<FinanceController> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly IOptionsMonitor<Settings> _settings = settings;
        private readonly IMyResponseFactory _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        private readonly AzureSQLDBContext _DBContext = DBContext;
        private readonly IUserService _userService = userService;
        private readonly ITWSEOpenAPIService _twseApiService = twseApiService ?? throw new ArgumentNullException(nameof(twseApiService));
        private readonly ITWSEDataService _twseDataService = twseDataService ?? throw new ArgumentNullException(nameof(twseDataService));
        private readonly IFinanceAnalysisService _financeAnalysisService = financeAnalysisService ?? throw new ArgumentNullException(nameof(financeAnalysisService));
        private readonly IExtendedMemoryCache _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        
        private const int OPERATION_TIMEOUT_SECONDS = 300; // 5 minutes
        private const int CACHE_HOURS = 12; // Cache duration in hours

        /// <summary>
        /// Gets the daily important financial information from Taiwan Stock Exchange.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for request timeout control.</param>
        /// <returns>An IActionResult containing the strategic insights or an error response.</returns>
        [HttpGet("dailyimportantinfo")]
        public async Task<IActionResult> GetDailyImportantInfo(CancellationToken cancellationToken = default)
        {
            // Create cache key based on current date to ensure daily refresh
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"DailyImportantInfo_{currentDate}";

            // Try to get from cache first
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                _logger.LogInformation("Returning cached daily important info for date: {date}", currentDate);
                return _responseFactory.CreateOKResponse(cachedResult);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));
            
            try
            {
                _logger.LogInformation("Fetching new daily important info for date: {date}", currentDate);
                // Step 1: Fetch raw material information from Taiwan Stock Exchange API with caching
                string rawMaterialInfo = await _twseDataService.GetRawMaterialInfoWithCacheAsync(timeoutCts.Token);
                if (string.IsNullOrEmpty(rawMaterialInfo))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to fetch data from Taiwan Stock Exchange API.");
                }

                var date = rawMaterialInfo.Substring(11, 7);
                var fileName = $"{date}.json";

                // Step 2: Get or upload file with optimized file management
                string fileId = await _twseDataService.GetOrUploadFileWithRetryAsync(rawMaterialInfo, fileName, timeoutCts.Token);
                if (string.IsNullOrEmpty(fileId))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to manage file in OpenAI.");
                }

                // Step 3: Ensure vector store is ready with optimized operations
                bool vectorStoreReady = await _twseDataService.EnsureVectorStoreReadyAsync(fileId, timeoutCts.Token);
                if (!vectorStoreReady)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, "Failed to prepare vector store.");
                }

                // Step 4: Create thread and run analysis with timeout
                var analysisResult = await _financeAnalysisService.RunFinancialAnalysisAsync(fileId, timeoutCts.Token);
                if (analysisResult == null)
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, "Analysis failed or timed out.");
                }

                // Cache the successful result for 4 hours
                _memoryCache.Set(cacheKey, analysisResult, TimeSpan.FromHours(CACHE_HOURS));
                
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
        /// Analyzes a specific stock using TWSE APIs and OpenAI.
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
                var (analysis, errorDetail) = await _financeAnalysisService.AnalyzeStockWithAIAsync(resolvedStockCode, stockData, timeoutCts.Token);
                if (analysis == null)
                {
                    var detailedError = $"Failed to analyze stock data for '{resolvedStockCode}'. Details: {errorDetail ?? "Unknown error"}";
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, detailedError);
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
    }
}