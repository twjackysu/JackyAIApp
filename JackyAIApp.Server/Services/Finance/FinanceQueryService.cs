using System.Text;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;

namespace JackyAIApp.Server.Services.Finance
{
    public class FinanceQueryService(
        ILogger<FinanceQueryService> logger,
        IUserService userService,
        ITWSEOpenAPIService twseApiService,
        IStockCodeResolver stockCodeResolver,
        ITWSEDataService twseDataService,
        IFinanceAnalysisService financeAnalysisService,
        IExtendedMemoryCache memoryCache,
        IMarketDataProvider marketDataProvider,
        IIndicatorEngine indicatorEngine,
        IChipDataProvider chipDataProvider,
        IStockScoreService stockScoreService,
        StockAnalysisBuilder analysisBuilder,
        IMacroEconomyProvider macroEconomyProvider,
        IMarketDataProviderFactory providerFactory) : IFinanceQueryService
    {
        private readonly ILogger<FinanceQueryService> _logger = logger;
        private readonly IUserService _userService = userService;
        private readonly ITWSEOpenAPIService _twseApiService = twseApiService;
        private readonly IStockCodeResolver _stockCodeResolver = stockCodeResolver;
        private readonly ITWSEDataService _twseDataService = twseDataService;
        private readonly IFinanceAnalysisService _financeAnalysisService = financeAnalysisService;
        private readonly IExtendedMemoryCache _memoryCache = memoryCache;
        private readonly IMarketDataProvider _marketDataProvider = marketDataProvider;
        private readonly IIndicatorEngine _indicatorEngine = indicatorEngine;
        private readonly IChipDataProvider _chipDataProvider = chipDataProvider;
        private readonly IStockScoreService _stockScoreService = stockScoreService;
        private readonly StockAnalysisBuilder _analysisBuilder = analysisBuilder;
        private readonly IMacroEconomyProvider _macroEconomyProvider = macroEconomyProvider;
        private readonly IMarketDataProviderFactory _providerFactory = providerFactory;

        private const int OPERATION_TIMEOUT_SECONDS = 300; // 5 minutes
        private const int CACHE_HOURS = 12;

        public async Task<ServiceResult> GetDailyImportantInfoAsync(CancellationToken cancellationToken)
        {
            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"DailyImportantInfo_{currentDate}";

            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                _logger.LogInformation("Returning cached daily important info for date: {date}", currentDate);
                return ServiceResult.Ok(cachedResult);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));

            try
            {
                _logger.LogInformation("Fetching new daily important info for date: {date}", currentDate);
                string rawMaterialInfo = await _twseDataService.GetRawMaterialInfoWithCacheAsync(timeoutCts.Token);
                if (string.IsNullOrEmpty(rawMaterialInfo))
                {
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, "Failed to fetch data from Taiwan Stock Exchange API.");
                }

                var analysisResult = await _financeAnalysisService.AnalyzeWithChatAPIAsync(rawMaterialInfo, timeoutCts.Token);
                if (analysisResult == null)
                {
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, "Analysis failed or timed out.");
                }

                _memoryCache.Set(cacheKey, analysisResult, TimeSpan.FromHours(CACHE_HOURS));
                return ServiceResult.Ok(analysisResult);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Operation timed out after {seconds} seconds", OPERATION_TIMEOUT_SECONDS);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Operation timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting daily important information.");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Internal server error occurred.");
            }
        }

        public async Task<ServiceResult> AnalyzeStockAsync(StockSearchRequest request, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));

            try
            {
                if (string.IsNullOrWhiteSpace(request.StockCodeOrName))
                {
                    return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code or name is required.");
                }

                var resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(request.StockCodeOrName, timeoutCts.Token);
                if (string.IsNullOrEmpty(resolvedStockCode))
                {
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Unable to find stock code for '{request.StockCodeOrName}'. Please verify the company name or stock code. Searched in TWSE company database but found no matches.");
                }

                var currentDate = DateTime.Now.ToString("yyyyMMdd");
                var cacheKey = $"StockAnalysis_{resolvedStockCode}_{currentDate}";

                if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                {
                    _logger.LogInformation("Returning cached stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);
                    return ServiceResult.Ok(cachedResult);
                }

                _logger.LogInformation("Fetching new stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);

                var stockData = await _twseApiService.GetStockDataAsync(resolvedStockCode, timeoutCts.Token);
                if (string.IsNullOrEmpty(stockData))
                {
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Failed to retrieve stock data from TWSE APIs for stock code '{resolvedStockCode}'. TWSE API endpoints may be unavailable or returned empty data.");
                }

                // Append quantitative analysis results to provide AI with structured indicators
                try
                {
                    var quantResult = await _analysisBuilder
                        .ForStock(resolvedStockCode)
                        .WithTechnical(true)
                        .WithChip(true)
                        .WithFundamental(true)
                        .WithScoring(true)
                        .WithRisk(true)
                        .BuildAsync(timeoutCts.Token);

                    stockData += FormatQuantitativeContext(quantResult);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Quantitative analysis failed for {stockCode}, proceeding with raw data only", resolvedStockCode);
                }

                var userId = _userService.GetUserId() ?? "anonymous";

                var (analysis, errorDetail) = await _financeAnalysisService.AnalyzeStockWithAIAsync(resolvedStockCode, stockData, userId, timeoutCts.Token);
                if (analysis == null)
                {
                    var detailedError = $"Failed to analyze stock data for '{resolvedStockCode}'. Details: {errorDetail ?? "Unknown error"}";
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, detailedError);
                }

                _memoryCache.Set(cacheKey, analysis, TimeSpan.FromHours(CACHE_HOURS));
                return ServiceResult.Ok(analysis);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("Stock analysis timed out for {stockCode}", request.StockCodeOrName);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Analysis timed out after {OPERATION_TIMEOUT_SECONDS} seconds for stock '{request.StockCodeOrName}'. Please try again later.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while analyzing stock {stockCode}", request.StockCodeOrName);
                var detailedError = $"Unexpected error occurred while analyzing stock '{request.StockCodeOrName}': {ex.Message}";
                if (ex.InnerException != null)
                {
                    detailedError += $" Inner exception: {ex.InnerException.Message}";
                }
                return ServiceResult.Fail(ErrorCodes.InternalServerError, detailedError);
            }
        }

        public async Task<ServiceResult> GetTechnicalIndicatorsAsync(string stockCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");
            }

            var resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"TechnicalIndicators_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return ServiceResult.Ok(cachedResult);
            }

            var marketData = await _marketDataProvider.FetchAsync(resolvedStockCode, cancellationToken);
            if (marketData.HistoricalPrices.Count == 0)
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"No historical data available for stock '{resolvedStockCode}'.");
            }

            var context = new IndicatorContext
            {
                StockCode = resolvedStockCode,
                Prices = marketData.HistoricalPrices
            };

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

            _memoryCache.Set(cacheKey, response, TimeSpan.FromHours(CACHE_HOURS));
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult> GetChipAnalysisAsync(string stockCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");
            }

            var resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"ChipAnalysis_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return ServiceResult.Ok(cachedResult);
            }

            var chipMarketData = await _chipDataProvider.FetchAsync(resolvedStockCode, cancellationToken);
            if (chipMarketData.Chips == null)
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"No chip data available for stock '{resolvedStockCode}'.");
            }

            var context = new IndicatorContext
            {
                StockCode = resolvedStockCode,
                Chips = chipMarketData.Chips
            };

            var indicators = _indicatorEngine.CalculateByCategory(context, IndicatorCategory.Chip);

            var response = new ChipAnalysisResponse
            {
                StockCode = resolvedStockCode,
                CompanyName = chipMarketData.CompanyName,
                ChipData = chipMarketData.Chips,
                Indicators = indicators,
                GeneratedAt = DateTime.UtcNow
            };

            _memoryCache.Set(cacheKey, response, TimeSpan.FromHours(CACHE_HOURS));
            return ServiceResult.Ok(response);
        }

        public async Task<ServiceResult> GetStockScoreAsync(string stockCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
            {
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");
            }

            var resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(stockCode, cancellationToken);
            if (string.IsNullOrEmpty(resolvedStockCode))
            {
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{stockCode}'.");
            }

            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var cacheKey = $"StockScore_{resolvedStockCode}_{currentDate}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
            {
                return ServiceResult.Ok(cachedResult);
            }

            try
            {
                var scoreResponse = await _stockScoreService.ScoreAsync(resolvedStockCode, cancellationToken);
                _memoryCache.Set(cacheKey, scoreResponse, TimeSpan.FromHours(CACHE_HOURS));
                return ServiceResult.Ok(scoreResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scoring stock {stockCode}", resolvedStockCode);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to score stock '{resolvedStockCode}': {ex.Message}");
            }
        }

        public async Task<ServiceResult> GetComprehensiveAnalysisAsync(StockAnalysisRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.StockCode))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");

            var region = !string.IsNullOrEmpty(request.Market)
                ? Enum.TryParse<MarketRegion>(request.Market, true, out var m) ? m : _providerFactory.DetectRegion(request.StockCode)
                : _providerFactory.DetectRegion(request.StockCode);

            string resolvedStockCode;
            if (region == MarketRegion.TW)
            {
                resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(request.StockCode, cancellationToken) ?? "";
                if (string.IsNullOrEmpty(resolvedStockCode))
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Unable to resolve stock code for '{request.StockCode}'.");
            }
            else
            {
                resolvedStockCode = request.StockCode.Trim().ToUpperInvariant();
            }

            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var configHash = $"{region}_{request.IncludeTechnical}_{request.IncludeChip}_{request.IncludeFundamental}_{request.IncludeScoring}_{request.IncludeRisk}_{request.TechnicalWeight}_{request.ChipWeight}_{request.FundamentalWeight}";
            var cacheKey = $"ComprehensiveAnalysis_{resolvedStockCode}_{currentDate}_{configHash}";

            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                return ServiceResult.Ok(cachedResult);

            try
            {
                var builder = _analysisBuilder
                    .ForStock(resolvedStockCode)
                    .ForMarket(region)
                    .WithTechnical(request.IncludeTechnical)
                    .WithChip(request.IncludeChip)
                    .WithFundamental(request.IncludeFundamental)
                    .WithScoring(request.IncludeScoring)
                    .WithRisk(request.IncludeRisk);

                if (request.OnlyIndicators?.Count > 0)
                    builder.OnlyIndicators(request.OnlyIndicators.ToArray());
                if (request.ExcludeIndicators?.Count > 0)
                    builder.ExcludeIndicators(request.ExcludeIndicators.ToArray());
                if (request.TechnicalWeight.HasValue || request.ChipWeight.HasValue || request.FundamentalWeight.HasValue)
                    builder.WithCustomWeights(request.TechnicalWeight, request.ChipWeight, request.FundamentalWeight);

                var result = await builder.BuildAsync(cancellationToken);
                result.Market = region.ToString();
                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comprehensive analysis failed for {stockCode} ({region})", resolvedStockCode, region);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Analysis failed for '{resolvedStockCode}': {ex.Message}");
            }
        }

        public async Task<ServiceResult> GetMacroEconomyAsync(CancellationToken cancellationToken)
        {
            var cacheKey = $"MacroEconomy_{DateTime.Now:yyyyMMdd}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                return ServiceResult.Ok(cachedResult);

            try
            {
                var result = await _macroEconomyProvider.FetchAsync(cancellationToken);
                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(2));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch macro economy data");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to fetch macro economy data: {ex.Message}");
            }
        }

        /// <summary>
        /// Formats quantitative analysis results into a text block for AI context.
        /// </summary>
        private static string FormatQuantitativeContext(StockAnalysisResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("=== QUANTITATIVE INDICATOR ANALYSIS (量化指標分析) ===");
            sb.AppendLine($"Stock: {result.CompanyName} ({result.StockCode})");
            sb.AppendLine($"Latest Close: {result.LatestClose?.ToString("F2") ?? "N/A"}");
            sb.AppendLine($"Data Range: {result.DataRange}");
            sb.AppendLine();

            var grouped = result.Indicators
                .GroupBy(i => i.Category)
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                sb.AppendLine($"--- {group.Key} Indicators ---");
                foreach (var ind in group)
                {
                    sb.AppendLine($"  {ind.Name}: Value={ind.Value:F2}, Score={ind.Score}/100, Direction={ind.Direction}, Signal={ind.Signal}");
                    if (!string.IsNullOrEmpty(ind.Reason))
                        sb.AppendLine($"    Reason: {ind.Reason}");
                }
                sb.AppendLine();
            }

            if (result.Scoring != null)
            {
                sb.AppendLine("--- Overall Scoring ---");
                sb.AppendLine($"  Overall Score: {result.Scoring.OverallScore}/100");
                sb.AppendLine($"  Overall Direction: {result.Scoring.OverallDirection}");
                sb.AppendLine($"  Recommendation: {result.Scoring.Recommendation}");
                foreach (var cs in result.Scoring.CategoryScores)
                {
                    sb.AppendLine($"  {cs.Category}: Score={cs.Score}, Weight={cs.Weight:P0}, Weighted={cs.WeightedScore:F1}");
                }
                sb.AppendLine();
            }

            if (result.Risk != null)
            {
                sb.AppendLine("--- Risk Assessment ---");
                sb.AppendLine($"  Risk Level: {result.Risk.Level}");
                sb.AppendLine($"  Divergence Score: {result.Risk.DivergenceScore:F2}");
                foreach (var factor in result.Risk.Factors)
                {
                    sb.AppendLine($"  ⚠ {factor}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
