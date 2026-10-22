using System.Text;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.Prediction;
using JackyAIApp.Server.Services.Finance.Scoring;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.Finance
{
    public class StockAnalysisQueryService(
        ILogger<StockAnalysisQueryService> logger,
        ICurrentUser currentUser,
        ITwseAnalysisData twseAnalysisData,
        IStockCodeResolver stockCodeResolver,
        IFinanceAnalysisService financeAnalysisService,
        IMemoryCache memoryCache,
        IMarketDataProvider marketDataProvider,
        IIndicatorEngine indicatorEngine,
        IChipDataProvider chipDataProvider,
        IStockScoreService stockScoreService,
        StockAnalysisBuilder analysisBuilder,
        IMarketIndexHistoryProvider marketIndexHistoryProvider,
        IMarketDataProviderFactory providerFactory,
        IStockPredictionRecorder predictionRecorder) : IStockAnalysisQueryService
    {
        private readonly ILogger<StockAnalysisQueryService> _logger = logger;
        private readonly ICurrentUser _currentUser = currentUser;
        private readonly ITwseAnalysisData _twseAnalysisData = twseAnalysisData;
        private readonly IStockCodeResolver _stockCodeResolver = stockCodeResolver;
        private readonly IFinanceAnalysisService _financeAnalysisService = financeAnalysisService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly IMarketDataProvider _marketDataProvider = marketDataProvider;
        private readonly IIndicatorEngine _indicatorEngine = indicatorEngine;
        private readonly IChipDataProvider _chipDataProvider = chipDataProvider;
        private readonly IStockScoreService _stockScoreService = stockScoreService;
        private readonly StockAnalysisBuilder _analysisBuilder = analysisBuilder;
        private readonly IMarketIndexHistoryProvider _marketIndexHistoryProvider = marketIndexHistoryProvider;
        private readonly IMarketDataProviderFactory _providerFactory = providerFactory;
        private readonly IStockPredictionRecorder _predictionRecorder = predictionRecorder;

        // Was 300s (5 min) — far longer than any user-facing request should ever take, and long
        // enough for a stuck analysis to tie up a thread and pile up under load. Data fetches now
        // run in parallel with a bounded per-call HTTP timeout, so 60s comfortably covers the
        // slowest external call plus the OpenAI analysis (incl. its retries).
        private const int OPERATION_TIMEOUT_SECONDS = 60;
        private const int CACHE_HOURS = 12;

        public async Task<ServiceResult<StockTrendAnalysis>> AnalyzeStockAsync(StockSearchRequest request, CancellationToken cancellationToken)
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

                if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is StockTrendAnalysis cachedResult)
                {
                    _logger.LogInformation("Returning cached stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);
                    return ServiceResult.Ok(cachedResult);
                }

                _logger.LogInformation("Fetching new stock analysis for stock: {stockCode}, date: {date}", resolvedStockCode, currentDate);

                // Gather the raw TWSE data and the quantitative indicators concurrently — they are
                // independent (and share a date-keyed cache), so there's no reason to run them in
                // series. The quant build is best-effort: failure just omits the structured context.
                var stockDataTask = _twseAnalysisData.DescribeAsync(resolvedStockCode, timeoutCts.Token);
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

                var userId = _currentUser.Id ?? "anonymous";

                var analysisResult = await _financeAnalysisService.AnalyzeStockWithAIAsync(resolvedStockCode, stockData, userId, timeoutCts.Token);
                if (!analysisResult.IsSuccess)
                {
                    var detailedError = $"Failed to analyze stock data for '{resolvedStockCode}'. Details: {analysisResult.ErrorMessage}";
                    return ServiceResult.Fail(analysisResult.Error.Value, detailedError);
                }
                var analysis = analysisResult.Data;

                // Pure additive side effect (prediction-accuracy tracking, TASK-013) — persists
                // 3 per-horizon rows so the trend calls above can later be checked against actual
                // price movement. Never fails the response: see StockPredictionRecorder.
                var predictionMarket = _providerFactory.DetectRegion(resolvedStockCode).ToString();
                await _predictionRecorder.RecordAsync(resolvedStockCode, predictionMarket, userId, analysis, timeoutCts.Token);

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
                return ServiceResult.Fail(ErrorCodes.InternalServerError,
                    $"Unexpected error occurred while analyzing stock '{request.StockCodeOrName}'.");
            }
        }

        public async Task<ServiceResult<TechnicalAnalysisResponse>> GetTechnicalIndicatorsAsync(string stockCode, CancellationToken cancellationToken)
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
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is TechnicalAnalysisResponse cachedResult)
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

        public async Task<ServiceResult<ChipAnalysisResponse>> GetChipAnalysisAsync(string stockCode, CancellationToken cancellationToken)
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
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is ChipAnalysisResponse cachedResult)
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

        public async Task<ServiceResult<StockScoreResponse>> GetStockScoreAsync(string stockCode, CancellationToken cancellationToken)
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
            if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is StockScoreResponse cachedResult)
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
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Failed to score stock '{resolvedStockCode}'.");
            }
        }

        /// <summary>
        /// The market a request is for — the explicit <paramref name="market"/> if it names one, else
        /// detected from the code — and the code to use with it. A TW code or company name goes through
        /// the resolver and fails with <paramref name="unresolvedError"/> when it matches nothing; a US
        /// code is just normalised. The two callers differ only in that error code (comprehensive
        /// analysis reports an upstream failure, the chart's price bars a plain not-found).
        /// </summary>
        private async Task<(MarketRegion Region, string Code, ServiceFailure? Unresolved)> ResolveStockAsync(
            string code, string? market, ErrorCodes unresolvedError, CancellationToken ct)
        {
            var region = Enum.TryParse<MarketRegion>(market, true, out var parsed)
                ? parsed
                : _providerFactory.DetectRegion(code);
            if (region != MarketRegion.TW)
                return (region, code.Trim().ToUpperInvariant(), null);

            var resolved = await _stockCodeResolver.ResolveStockCodeAsync(code, ct) ?? "";
            return string.IsNullOrEmpty(resolved)
                ? (region, "", ServiceResult.Fail(unresolvedError, $"Unable to resolve stock code for '{code}'."))
                : (region, resolved, null);
        }

        public async Task<ServiceResult<StockAnalysisResult>> GetComprehensiveAnalysisAsync(StockAnalysisRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.StockCode))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");

            var (region, resolvedStockCode, unresolved) = await ResolveStockAsync(
                request.StockCode, request.Market, ErrorCodes.ExternalApiError, cancellationToken);
            if (unresolved is { } failure) return failure;

            var currentDate = DateTime.Now.ToString("yyyyMMdd");
            var configHash = $"{region}_{request.IncludeTechnical}_{request.IncludeChip}_{request.IncludeFundamental}_{request.IncludeScoring}_{request.IncludeRisk}_{request.TechnicalWeight}_{request.ChipWeight}_{request.FundamentalWeight}";
            var cacheKey = $"ComprehensiveAnalysis_{resolvedStockCode}_{currentDate}_{configHash}";

            if (_memoryCache.TryGetValue(cacheKey, out object? cachedEntry) && cachedEntry is StockAnalysisResult cachedResult)
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

                // No price and no indicator at all: the code doesn't exist, or every upstream
                // came back empty (the TW price chain swallows its own exceptions into an empty
                // MarketData). Say "not found" rather than succeed with a blank page, and never
                // cache it — an upstream blip would otherwise freeze this stock blank for 12 hours.
                if (result.LatestClose is null && result.Indicators.Count == 0)
                {
                    _logger.LogWarning("Comprehensive analysis found no market data for {stockCode} ({region})", resolvedStockCode, region);
                    return ServiceResult.Fail(ErrorCodes.NotFound, $"No market data found for '{resolvedStockCode}'.");
                }

                // A missing price is most likely transient too, so a partial result is served but
                // not cached; only results that carry a price are held for CACHE_HOURS.
                if (result.LatestClose is not null)
                    _memoryCache.Set(cacheKey, result, TimeSpan.FromHours(CACHE_HOURS));
                return ServiceResult.Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Comprehensive analysis failed for {stockCode} ({region})", resolvedStockCode, region);
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Analysis failed for '{resolvedStockCode}'.");
            }
        }

        /// <summary>Fewest bars worth drawing trendlines on.</summary>
        internal const int MinPriceBars = 30;

        public async Task<ServiceResult<PriceBarsResponse>> GetPriceBarsAsync(string code, string? market, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");

            if (code.Trim().Equals(TaiexCode, StringComparison.OrdinalIgnoreCase))
                return await GetTaiexPriceBarsAsync(cancellationToken);

            var (region, resolvedStockCode, unresolved) = await ResolveStockAsync(
                code, market, ErrorCodes.NotFound, cancellationToken);
            if (unresolved is { } failure) return failure;

            try
            {
                // The same cached provider the comprehensive analysis reads, so opening the chart after
                // an analysis costs no extra upstream call.
                var data = await _providerFactory.GetMarketDataProvider(region).FetchAsync(resolvedStockCode, cancellationToken);
                var bars = data.HistoricalPrices
                    .Where(p => p.Open > 0 && p.High > 0 && p.Low > 0 && p.Close > 0 && p.High >= p.Low)
                    .GroupBy(p => DateOnly.FromDateTime(p.Date))
                    .Select(g => g.Last())
                    .OrderBy(p => p.Date)
                    // A bar with no volume is one the persistent layer back-filled from a quote
                    // (flat at the close, volume unknown), not a day nothing traded.
                    .Select(p => new PriceBar(
                        DateOnly.FromDateTime(p.Date), p.Open, p.High, p.Low, p.Close, p.Volume > 0 ? p.Volume : null))
                    .ToList();
                if (bars.Count < MinPriceBars)
                    return ServiceResult.Fail(ErrorCodes.NotFound, $"Not enough price history for '{resolvedStockCode}'.");

                var name = string.IsNullOrWhiteSpace(data.CompanyName) ? resolvedStockCode : data.CompanyName;
                return ServiceResult.Ok(new PriceBarsResponse(resolvedStockCode, name, bars[^1].Date, bars));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load price bars for {stockCode} ({region})", resolvedStockCode, region);
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, $"Failed to load prices for '{resolvedStockCode}'.");
            }
        }

        private const string TaiexCode = "TAIEX";

        /// <summary>
        /// The whole cached six-year TAIEX series; the chart picks its own start date out of it. A day
        /// the source gave no usable range for becomes a flat bar at its close.
        /// </summary>
        private async Task<ServiceResult<PriceBarsResponse>> GetTaiexPriceBarsAsync(CancellationToken cancellationToken)
        {
            var history = await _marketIndexHistoryProvider.GetAsync(cancellationToken);
            if (history is null)
                return ServiceResult.Fail(ErrorCodes.ExternalApiError, "TAIEX history is currently unavailable.");

            var bars = history.Points
                .Select(p => p.Open is { } o && p.High is { } h && p.Low is { } l
                    ? new PriceBar(p.Date, o, h, l, p.Close)
                    : new PriceBar(p.Date, p.Close, p.Close, p.Close, p.Close))
                .ToList();
            return ServiceResult.Ok(new PriceBarsResponse(TaiexCode, history.Name, history.AsOf, bars));
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
