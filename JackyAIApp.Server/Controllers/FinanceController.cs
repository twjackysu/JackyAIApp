using JackyAIApp.Server.Common;
using JackyAIApp.Server.Configuration;
using JackyAIApp.Server.Data;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;
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
        IExtendedMemoryCache memoryCache,
        IMarketDataProvider marketDataProvider,
        IIndicatorEngine indicatorEngine,
        TWSEChipDataProvider chipDataProvider,
        IStockScoreService stockScoreService) : ControllerBase
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
        private readonly IMarketDataProvider _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
        private readonly IIndicatorEngine _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
        private readonly TWSEChipDataProvider _chipDataProvider = chipDataProvider ?? throw new ArgumentNullException(nameof(chipDataProvider));
        private readonly IStockScoreService _stockScoreService = stockScoreService ?? throw new ArgumentNullException(nameof(stockScoreService));

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

                // Step 2: Run analysis using Chat API with chunked processing (more cost-effective)
                var analysisResult = await _financeAnalysisService.AnalyzeWithChatAPIAsync(rawMaterialInfo, timeoutCts.Token);
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

                // Create cache key based on resolved stock code and current date
                var currentDate = DateTime.Now.ToString("yyyyMMdd");
                var cacheKey = $"StockAnalysis_{resolvedStockCode}_{currentDate}";

                // Try to get from cache first
                if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                {
                    _logger.LogInformation("Returning cached stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);
                    return _responseFactory.CreateOKResponse(cachedResult);
                }

                _logger.LogInformation("Fetching new stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);

                // Call TWSE Open API service to get stock data using resolved stock code
                var stockData = await _twseApiService.GetStockDataAsync(resolvedStockCode, timeoutCts.Token);
                if (string.IsNullOrEmpty(stockData))
                {
                    return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Failed to retrieve stock data from TWSE APIs for stock code '{resolvedStockCode}'. TWSE API endpoints may be unavailable or returned empty data.");
                }

                // Get user ID for Dify API
                var userId = _userService.GetUserId() ?? "anonymous";

                // Analyze the stock data using Dify AI with the resolved stock code
                var (analysis, errorDetail) = await _financeAnalysisService.AnalyzeStockWithAIAsync(resolvedStockCode, stockData, userId, timeoutCts.Token);
                if (analysis == null)
                {
                    var detailedError = $"Failed to analyze stock data for '{resolvedStockCode}'. Details: {errorDetail ?? "Unknown error"}";
                    return _responseFactory.CreateErrorResponse(ErrorCodes.QueryOpenAIFailed, detailedError);
                }

                // Cache the successful result for the same duration as daily important info
                _memoryCache.Set(cacheKey, analysis, TimeSpan.FromHours(CACHE_HOURS));

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
        /// Gets technical indicators for a specific stock using historical data.
        /// Uses TWStockLib for data and calculates MA, RSI, MACD, KD, Volume, Bollinger Bands.
        /// </summary>
        /// <param name="stockCode">Stock code (e.g., "2330")</param>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>Technical analysis with all calculated indicators.</returns>
        [HttpGet("technical-indicators/{stockCode}")]
        public async Task<IActionResult> GetTechnicalIndicators(string stockCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Stock code is required.");
            }

            // Resolve stock code if needed
            var resolvedStockCode = await _twseApiService.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            // Check cache
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"TechnicalIndicators_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return _responseFactory.CreateOKResponse(cachedResult);
            }

            // Fetch historical data via TWStockLib (cached by CachedMarketDataProvider)
            var marketData = await _marketDataProvider.FetchAsync(resolvedStockCode, cancellationToken);
            if (marketData.HistoricalPrices.Count == 0)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"No historical data available for stock '{resolvedStockCode}'.");
            }

            // Build indicator context
            var context = new IndicatorContext
            {
                StockCode = resolvedStockCode,
                Prices = marketData.HistoricalPrices
            };

            // Calculate all technical indicators
            var indicators = _indicatorEngine.CalculateByCategory(context, IndicatorCategory.Technical);

            var response = new TechnicalAnalysisResponse
            {
                StockCode = resolvedStockCode,
                CompanyName = marketData.CompanyName,
                LatestClose = context.LatestClose,
                DataPointCount = marketData.HistoricalPrices.Count,
                DataRange = $"{marketData.HistoricalPrices.First().Date:yyyy-MM-dd} ~ {marketData.HistoricalPrices.Last().Date:yyyy-MM-dd}",
                Indicators = indicators,
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 4 hours
            _memoryCache.Set(cacheKey, response, TimeSpan.FromHours(CACHE_HOURS));

            return _responseFactory.CreateOKResponse(response);
        }

        /// <summary>
        /// Gets chip analysis (籌碼面) indicators for a specific stock.
        /// Includes margin trading, foreign holdings, director pledges, and major shareholders.
        /// </summary>
        /// <param name="stockCode">Stock code (e.g., "2330")</param>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>Chip analysis with all calculated indicators.</returns>
        [HttpGet("chip-analysis/{stockCode}")]
        public async Task<IActionResult> GetChipAnalysis(string stockCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Stock code is required.");
            }

            // Resolve stock code if needed
            var resolvedStockCode = await _twseApiService.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            // Check cache
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"ChipAnalysis_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return _responseFactory.CreateOKResponse(cachedResult);
            }

            // Fetch chip data from TWSE APIs
            var chipMarketData = await _chipDataProvider.FetchAsync(resolvedStockCode, cancellationToken);

            if (chipMarketData.Chips == null)
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"No chip data available for stock '{resolvedStockCode}'.");
            }

            // Build indicator context with chip data
            var context = new IndicatorContext
            {
                StockCode = resolvedStockCode,
                Chips = chipMarketData.Chips
            };

            // Calculate chip indicators
            var indicators = _indicatorEngine.CalculateByCategory(context, IndicatorCategory.Chip);

            var response = new ChipAnalysisResponse
            {
                StockCode = resolvedStockCode,
                CompanyName = chipMarketData.CompanyName,
                ChipData = chipMarketData.Chips,
                Indicators = indicators,
                GeneratedAt = DateTime.UtcNow
            };

            // Cache for 4 hours
            _memoryCache.Set(cacheKey, response, TimeSpan.FromHours(CACHE_HOURS));

            return _responseFactory.CreateOKResponse(response);
        }

        /// <summary>
        /// Gets a comprehensive stock score combining technical, chip, and fundamental indicators.
        /// Returns a weighted composite score (0-100) with risk assessment and recommendation.
        /// </summary>
        /// <param name="stockCode">Stock code (e.g., "2330")</param>
        /// <param name="cancellationToken">Cancellation token for timeout control.</param>
        /// <returns>Comprehensive stock scoring with breakdown by category.</returns>
        [HttpGet("score/{stockCode}")]
        public async Task<IActionResult> GetStockScore(string stockCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, "Stock code is required.");
            }

            // Resolve stock code if needed
            var resolvedStockCode = await _twseApiService.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return _responseFactory.CreateErrorResponse(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            // Check cache
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"StockScore_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return _responseFactory.CreateOKResponse(cachedResult);
            }

            try
            {
                var scoreResponse = await _stockScoreService.ScoreAsync(resolvedStockCode, cancellationToken);

                // Cache for 4 hours
                _memoryCache.Set(cacheKey, scoreResponse, TimeSpan.FromHours(CACHE_HOURS));

                return _responseFactory.CreateOKResponse(scoreResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scoring stock {stockCode}", resolvedStockCode);
                return _responseFactory.CreateErrorResponse(ErrorCodes.InternalServerError, $"Failed to score stock '{resolvedStockCode}': {ex.Message}");
            }
        }
    }
}