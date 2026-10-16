using System.Text;
using DotnetSdkUtilities.Services;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO;
using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services;
using JackyAIApp.Server.Services.Finance.Prediction;
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
        IMarketIndexHistoryProvider marketIndexHistoryProvider,
        IIndicatorProvider indicatorProvider,
        IFinnhubNewsProvider finnhubNewsProvider,
        IOpenAIPromptService openAIPromptService,
        IUsageLimitService usageLimitService,
        IMarketDataProviderFactory providerFactory,
        IStockPredictionRecorder predictionRecorder) : IFinanceQueryService
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
        private readonly IMarketIndexHistoryProvider _marketIndexHistoryProvider = marketIndexHistoryProvider;
        private readonly IIndicatorProvider _indicatorProvider = indicatorProvider;
        private readonly IFinnhubNewsProvider _finnhubNewsProvider = finnhubNewsProvider;
        private readonly IOpenAIPromptService _openAIPromptService = openAIPromptService;
        private readonly IUsageLimitService _usageLimitService = usageLimitService;
        private readonly IMarketDataProviderFactory _providerFactory = providerFactory;
        private readonly IStockPredictionRecorder _predictionRecorder = predictionRecorder;

        // Was 300s (5 min) — far longer than any user-facing request should ever take, and long
        // enough for a stuck analysis to tie up a thread and pile up under load. Data fetches now
        // run in parallel with a bounded per-call HTTP timeout, so 60s comfortably covers the
        // slowest external call plus the OpenAI analysis (incl. its retries).
        private const int OPERATION_TIMEOUT_SECONDS = 60;
        private const int CACHE_HOURS = 12;

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
                return ServiceResult.Fail(ErrorCodes.InternalServerError, $"Analysis failed for '{resolvedStockCode}': {ex.Message}");
            }
        }

        /// <summary>Fewest bars worth drawing trendlines on.</summary>
        internal const int MinPriceBars = 30;

        public async Task<ServiceResult> GetPriceBarsAsync(string code, string? market, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code))
                return ServiceResult.Fail(ErrorCodes.BadRequest, "Stock code is required.");

            if (code.Trim().Equals(TaiexCode, StringComparison.OrdinalIgnoreCase))
                return await GetTaiexPriceBarsAsync(cancellationToken);

            var region = Enum.TryParse<MarketRegion>(market, true, out var m) ? m : _providerFactory.DetectRegion(code);
            string resolvedStockCode;
            if (region == MarketRegion.TW)
            {
                resolvedStockCode = await _stockCodeResolver.ResolveStockCodeAsync(code, cancellationToken) ?? "";
                if (string.IsNullOrEmpty(resolvedStockCode))
                    return ServiceResult.Fail(ErrorCodes.NotFound, $"Unable to resolve stock code for '{code}'.");
            }
            else
            {
                resolvedStockCode = code.Trim().ToUpperInvariant();
            }

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
        private async Task<ServiceResult> GetTaiexPriceBarsAsync(CancellationToken cancellationToken)
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

        /// <summary>
        /// Multi-year daily TAIEX closes for the "same period, one line per year" chart. Caching,
        /// single-flight and the last-good fallback live in the provider; this only maps "no data"
        /// to an error rather than ever returning an empty or partial series as a success.
        /// </summary>
        public async Task<ServiceResult> GetMarketIndexHistoryAsync(CancellationToken cancellationToken)
        {
            var history = await _marketIndexHistoryProvider.GetAsync(cancellationToken);
            return history is null
                ? ServiceResult.Fail(ErrorCodes.MarketHistoryUnavailable, "Market index history is temporarily unavailable.")
                : ServiceResult.Ok(history);
        }

        /// <summary>
        /// "今日市場AI摘要" — a unified daily digest grounding gpt-6-luna in three sources
        /// (market overview + global news + TW major announcements) and synthesising a
        /// global/Taiwan two-section narrative. The generated digest is shared (cached per
        /// day), but each user is charged <see cref="UsageCosts.MarketDigest"/> once per day
        /// to unlock it — repeat views the same day are free.
        /// </summary>
        public async Task<ServiceResult> GetMarketDigestAsync(CancellationToken cancellationToken)
        {
            var userId = _userService.GetUserId();
            if (string.IsNullOrEmpty(userId))
                return ServiceResult.Fail(ErrorCodes.Forbidden, "User not found.");

            var today = DateTime.Now.ToString("yyyyMMdd");

            // 1) Ensure today's digest content exists (shared across users).
            var contentKey = $"MarketDigest_{today}";
            var digest = _memoryCache.TryGetValue(contentKey, out object? cached) ? cached as MarketDigestResponse : null;
            if (digest is null)
            {
                var (generated, error) = await GenerateMarketDigestAsync(cancellationToken);
                if (generated is null)
                    return ServiceResult.Fail(ErrorCodes.QueryOpenAIFailed, error ?? "Failed to generate market digest.");
                digest = generated;
                _memoryCache.Set(contentKey, digest, TimeSpan.FromHours(CACHE_HOURS));
            }

            // 2) Charge once per user per day (cached reads the same day are free).
            var chargeKey = $"MarketDigestCharged_{userId}_{today}";
            if (!_memoryCache.TryGetValue(chargeKey, out object? _))
            {
                var ok = await _usageLimitService.TryConsumeAsync(userId, UsageCosts.MarketDigest, "market_digest");
                if (!ok)
                    return ServiceResult.Fail(ErrorCodes.UsageLimitExceeded, "Daily usage limit exceeded.");
                // Hold the guard until end of day so the user isn't charged again today.
                _memoryCache.Set(chargeKey, true, DateTime.Today.AddDays(1) - DateTime.Now);
            }

            return ServiceResult.Ok(digest);
        }

        /// <summary>
        /// Gathers the three grounding sources in parallel and calls gpt-6-luna to produce the
        /// unified digest. Pure generation — no caching or usage-limit logic.
        /// </summary>
        private async Task<(MarketDigestResponse? digest, string? error)> GenerateMarketDigestAsync(CancellationToken cancellationToken)
        {
            // Fetch the three sources in parallel. News + TW announcements are best-effort.
            var overviewTask = GetMarketOverviewAsync(cancellationToken);
            var newsTask = _finnhubNewsProvider.IsConfigured
                ? _finnhubNewsProvider.FetchAsync(cancellationToken)
                : Task.FromResult<IReadOnlyList<FinnhubNewsItem>>([]);
            var twseTask = GetTwseMajorNewsTextSafeAsync(cancellationToken);

            await Task.WhenAll(overviewTask, newsTask, twseTask);

            var overviewResult = overviewTask.Result;
            if (!overviewResult.IsSuccess)
                return (null, overviewResult.ErrorMessage);

            var overview = (MarketOverviewResponse)overviewResult.Data!;
            if (overview.Quotes.Count == 0)
                return (null, "No market data available for the digest.");

            var dataText = string.Join("\n", overview.Quotes.Select(q =>
            {
                var price = q.Price?.ToString("0.##") ?? "N/A";
                var pct = q.ChangePercent.HasValue
                    ? $" ({(q.ChangePercent.Value >= 0 ? "+" : "")}{q.ChangePercent.Value:F2}%)"
                    : "";
                return $"{q.Name}: {price}{pct}";
            }));

            var news = newsTask.Result;
            var newsSection = news.Count > 0
                ? "\n\n全球財經新聞（用於解讀全球市場移動原因）：\n" +
                  string.Join("\n", news.Select((n, i) =>
                      $"{i + 1}. [{n.Source}] {n.Headline}" +
                      (string.IsNullOrWhiteSpace(n.Summary) ? "" : $"：{n.Summary}")))
                : "";

            var twseSection = string.IsNullOrWhiteSpace(twseTask.Result)
                ? ""
                : $"\n\n台股今日公司公告（證交所公開資訊觀測站重大訊息，多為更名/處分資產/董事會決議/契約等公開公告，非投資訊號；日期為民國年需 +1911 轉西元）：\n{twseTask.Result}";

            var systemPrompt =
                "你是專業市場策略師，為台灣投資人整理『今日市場AI摘要』。根據提供的三類資料（全球市場數據、全球財經新聞、台股公司公告），" +
                "整理成兩區重點：\n" +
                "- category=\"global\"（全球總經）：3~5 點，挖掘數字與新聞背後的因果與情緒，不要只複述數字。\n" +
                "- category=\"taiwan\"（台股公司動態）：2~4 點。注意這些是公開公告（更名、處分股權/資產、董事會決議、重大契約、併購等），" +
                "不是投資訊號；請挑出較具實質意義的（如併購、處分重大資產、重大契約、營運變動），過濾純例行（補充說明、財報公布日、更名等行政事項），" +
                "並如實描述公告內容、不要誇大成買賣建議；多數情況 sentiment 用 neutral，除非公告明顯偏多或偏空。若是特定公司，填 stockCode。\n" +
                "每點：headline（一句標題）+ detail（一句說明）+ sentiment（bullish|bearish|neutral）+ category。" +
                "只根據提供的資料推論，不編造未提供的事件。全部用繁體中文。" +
                "只回傳 JSON：{\"points\":[{\"category\":\"global|taiwan\",\"headline\":\"\",\"detail\":\"\",\"sentiment\":\"bullish|bearish|neutral\",\"stockCode\":\"\"}]}。";
            var userMessage = $"全球市場數據：\n{dataText}{newsSection}{twseSection}";

            try
            {
                var (result, error) = await _openAIPromptService.GetCompletionWithSystemPromptAsync<MarketDigestResponse>(
                    systemPrompt, userMessage, NewModels.GPT_6_LUNA, feature: "finance.market_digest");

                if (result is null || result.Points.Count == 0)
                    return (null, error ?? "Failed to generate market digest.");

                result.GeneratedAt = DateTime.UtcNow;
                return (result, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate market digest");
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// Fetches today's TWSE major-announcement filings and trims them to a compact, token-bounded
        /// text block for the digest prompt. Best-effort: returns empty on any failure.
        /// </summary>
        private async Task<string> GetTwseMajorNewsTextSafeAsync(CancellationToken cancellationToken)
        {
            const int MAX_ITEMS = 60;
            try
            {
                var raw = await _twseDataService.GetRawMaterialInfoWithCacheAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(raw))
                    return "";

                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return "";

                var lines = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (lines.Count >= MAX_ITEMS) break;

                    string Get(params string[] keys)
                    {
                        foreach (var k in keys)
                            if (item.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                                return v.GetString() ?? "";
                        return "";
                    }

                    var code = Get("公司代號", "Code", "CompanyCode");
                    var name = Get("公司名稱", "Name", "CompanyName");
                    var subject = Get("主旨", "Subject", "Title");
                    if (string.IsNullOrWhiteSpace(subject)) continue;

                    lines.Add($"{code} {name}：{subject}".Trim());
                }

                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build TWSE major-news text for digest");
                return "";
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
                    systemPrompt, userMessage, NewModels.GPT_6_LUNA, feature: "finance.indicator_insight");

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
