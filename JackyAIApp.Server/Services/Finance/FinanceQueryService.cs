using System.Text;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;
using JackyAIApp.Server.Services.OpenAI;

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
        IMarketOverviewProvider marketOverviewProvider,
        IIndicatorProvider indicatorProvider,
        IOpenAIPromptService openAIPromptService,
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
        private readonly IMarketOverviewProvider _marketOverviewProvider = marketOverviewProvider;
        private readonly IIndicatorProvider _indicatorProvider = indicatorProvider;
        private readonly IOpenAIPromptService _openAIPromptService = openAIPromptService;
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

                // Gather the raw TWSE data and the quantitative indicators concurrently — they are
                // independent (and share a date-keyed cache), so there's no reason to run them in
                // series. The quant build is best-effort: failure just omits the structured context.
                var stockDataTask = _twseApiService.GetStockDataAsync(resolvedStockCode, timeoutCts.Token);
                var quantTask = BuildQuantitativeContextSafeAsync(resolvedStockCode, timeoutCts.Token);
                await Task.WhenAll(stockDataTask, quantTask);

                var stockData = await stockDataTask;
                if (string.IsNullOrEmpty(stockData))
                {
                    return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Failed to retrieve stock data from TWSE APIs for stock code '{resolvedStockCode}'. TWSE API endpoints may be unavailable or returned empty data.");
                }

                var quantResult = await quantTask;
                if (quantResult != null)
                {
                    stockData += FormatQuantitativeContext(quantResult);
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

        public async Task<ServiceResult> GetMarketOverviewAsync(CancellationToken cancellationToken)
        {
            const string cacheKey = "MarketOverview";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                return ServiceResult.Ok(cachedResult);

            try
            {
                var result = await _marketOverviewProvider.FetchAsync(cancellationToken);
                // Short TTL — these are intraday snapshots, but cache enough to shield the upstream
                // (free, unofficial) source from per-request load.
                _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(3));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch market overview data");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to fetch market overview data: {ex.Message}");
            }
        }

        public async Task<ServiceResult> GetMarketBriefAsync(CancellationToken cancellationToken)
        {
            const string cacheKey = "MarketBrief";
            if (_memoryCache.TryGetValue(cacheKey, out object? cached))
                return ServiceResult.Ok(cached);

            // Ground the AI in the day's actual market data (reuses the cached overview).
            var overviewResult = await GetMarketOverviewAsync(cancellationToken);
            if (!overviewResult.IsSuccess)
                return overviewResult;

            var overview = (MarketOverviewResponse)overviewResult.Data!;
            if (overview.Quotes.Count == 0)
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "No market data available for the brief.");

            var dataText = string.Join("\n", overview.Quotes.Select(q =>
            {
                var price = q.Price?.ToString("0.##") ?? "N/A";
                var pct = q.ChangePercent.HasValue
                    ? $" ({(q.ChangePercent.Value >= 0 ? "+" : "")}{q.ChangePercent.Value:F2}%)"
                    : "";
                return $"{q.Name}: {price}{pct}";
            }));

            var systemPrompt =
                "你是專業市場策略師。根據提供的『今日市場數據（含當日漲跌幅）』，挑出 3~5 個今天真正重要、" +
                "值得投資人注意的重點。每個重點：一句精簡標題 + 一句白話說明，並標註偏多/偏空/中性。" +
                "請務必只根據提供的數據推論，不要編造未提供的事件（例如財報、發言、新聞）。全部用繁體中文。" +
                "只回傳 JSON：{\"points\":[{\"headline\":\"\",\"detail\":\"\",\"sentiment\":\"bullish|bearish|neutral\"}]}。";
            var userMessage = $"今日市場數據：\n{dataText}";

            try
            {
                var (result, error) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<MarketBriefResponse>(
                    systemPrompt, userMessage, NewModels.GPT_5_NANO);

                if (result is null || result.Points.Count == 0)
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, error ?? "Failed to generate market brief.");

                result.GeneratedAt = DateTime.UtcNow;
                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(3));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate market brief");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to generate market brief: {ex.Message}");
            }
        }

        public async Task<ServiceResult> GetIndicatorsAsync(CancellationToken cancellationToken)
        {
            if (!_indicatorProvider.IsConfigured)
                return ServiceResult.Fail(ErrorCodes.InternalServerError, "Indicator data source is not configured (FRED API key missing).");

            const string cacheKey = "Indicators";
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedResult))
                return ServiceResult.Ok(cachedResult);

            try
            {
                var result = await _indicatorProvider.FetchAsync(cancellationToken);
                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(6));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch indicators");
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to fetch indicators: {ex.Message}");
            }
        }

        public async Task<ServiceResult> GetIndicatorInsightAsync(string id, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(id))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Indicator id is required.");

            var cacheKey = $"IndicatorInsight_{id}_{DateTime.Today:yyyyMMdd}";
            if (_memoryCache.TryGetValue(cacheKey, out object? cached))
                return ServiceResult.Ok(cached);

            // Reuse the cached indicator set to build the prompt context.
            var indicatorsResult = await GetIndicatorsAsync(cancellationToken);
            if (!indicatorsResult.IsSuccess)
                return indicatorsResult;

            var indicators = (IndicatorExplorerResponse)indicatorsResult.Data!;
            var series = indicators.Indicators.FirstOrDefault(i => i.Id == id);
            if (series is null)
                return ServiceResult.Fail(ErrorCodes.NotFound, $"Indicator '{id}' not found.");

            // Summarise recent points (first / mid / last) so the prompt stays small.
            var pts = series.Points;
            var sampled = pts.Count <= 8 ? pts : new[] { pts[0], pts[pts.Count / 2], pts[^1] }.ToList();
            var pointsText = string.Join("\n", sampled.Select(p => $"{p.Date}: {p.Value}{series.Unit}"));

            var systemPrompt =
                "你是專業總體經濟分析師。根據提供的單一指標近期數據，用繁體中文寫 2~3 句白話解讀：" +
                "它目前在說什麼、趨勢方向、以及對市場或投資人的意涵。避免空泛、給具體含義。" +
                "只回傳 JSON：{\"insight\":\"...\"}。";
            var userMessage =
                $"指標：{series.Name}（{series.Description}）\n單位：{series.Unit}\n最新：{series.LatestValue}（{series.LatestDate}）\n近期數據：\n{pointsText}";

            try
            {
                var (result, error) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<IndicatorInsight>(
                    systemPrompt, userMessage, NewModels.GPT_5_NANO);

                if (result is null)
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, error ?? "Failed to generate insight.");

                _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate indicator insight for {id}", id);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to generate insight: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the full quantitative analysis for AI context. Best-effort: returns null (and
        /// logs) on failure so the AI analysis can still proceed with the raw TWSE data.
        /// </summary>
        private async Task<StockAnalysisResult?> BuildQuantitativeContextSafeAsync(string stockCode, CancellationToken ct)
        {
            try
            {
                return await _analysisBuilder
                    .ForStock(stockCode)
                    .WithTechnical(true)
                    .WithChip(true)
                    .WithFundamental(true)
                    .WithScoring(true)
                    .WithRisk(true)
                    .BuildAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Quantitative analysis failed for {stockCode}, proceeding with raw data only", stockCode);
                return null;
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
