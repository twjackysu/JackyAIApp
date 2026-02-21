using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;

namespace JackyAIApp.Server.Services.Finance.Builder
{
    /// <summary>
    /// Builder for composing stock analysis with flexible indicator selection.
    /// Uses Builder pattern to let callers pick categories, indicators, and weights.
    /// </summary>
    public class StockAnalysisBuilder
    {
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly IChipDataProvider _chipDataProvider;
        private readonly IFundamentalDataProvider _fundamentalDataProvider;
        private readonly IMarketDataProviderFactory _providerFactory;
        private readonly IIndicatorEngine _indicatorEngine;
        private readonly CategoryWeightConfig _weightConfig;
        private readonly ILogger<StockAnalysisBuilder> _logger;

        private string _stockCode = string.Empty;
        private bool _includeTechnical = true;
        private bool _includeChip = true;
        private bool _includeFundamental = true;
        private bool _includeScoring = true;
        private bool _includeRisk = true;
        private MarketRegion? _marketRegion;
        private readonly HashSet<string> _onlyIndicators = new();
        private readonly HashSet<string> _excludeIndicators = new();
        private readonly Dictionary<IndicatorCategory, decimal> _customWeights = new();

        public StockAnalysisBuilder(
            IMarketDataProvider marketDataProvider,
            IChipDataProvider chipDataProvider,
            IFundamentalDataProvider fundamentalDataProvider,
            IMarketDataProviderFactory providerFactory,
            IIndicatorEngine indicatorEngine,
            CategoryWeightConfig weightConfig,
            ILogger<StockAnalysisBuilder> logger)
        {
            _marketDataProvider = marketDataProvider;
            _chipDataProvider = chipDataProvider;
            _fundamentalDataProvider = fundamentalDataProvider;
            _providerFactory = providerFactory;
            _indicatorEngine = indicatorEngine;
            _weightConfig = weightConfig;
            _logger = logger;
        }

        public StockAnalysisBuilder ForStock(string stockCode) { _stockCode = stockCode; _marketRegion = null; return this; }
        public StockAnalysisBuilder ForMarket(MarketRegion region) { _marketRegion = region; return this; }
        public StockAnalysisBuilder WithTechnical(bool include = true) { _includeTechnical = include; return this; }
        public StockAnalysisBuilder WithChip(bool include = true) { _includeChip = include; return this; }
        public StockAnalysisBuilder WithFundamental(bool include = true) { _includeFundamental = include; return this; }
        public StockAnalysisBuilder WithScoring(bool include = true) { _includeScoring = include; return this; }
        public StockAnalysisBuilder WithRisk(bool include = true) { _includeRisk = include; return this; }

        public StockAnalysisBuilder OnlyIndicators(params string[] names)
        {
            foreach (var n in names) _onlyIndicators.Add(n);
            return this;
        }

        public StockAnalysisBuilder ExcludeIndicators(params string[] names)
        {
            foreach (var n in names) _excludeIndicators.Add(n);
            return this;
        }

        public StockAnalysisBuilder WithCustomWeights(decimal? technical = null, decimal? chip = null, decimal? fundamental = null)
        {
            if (technical.HasValue) _customWeights[IndicatorCategory.Technical] = technical.Value;
            if (chip.HasValue) _customWeights[IndicatorCategory.Chip] = chip.Value;
            if (fundamental.HasValue) _customWeights[IndicatorCategory.Fundamental] = fundamental.Value;
            return this;
        }

        public async Task<StockAnalysisResult> BuildAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_stockCode))
                throw new ArgumentException("Stock code is required. Call ForStock() first.");

            _logger.LogInformation("Building analysis for {stockCode}", _stockCode);

            // Resolve providers based on market region
            var region = _marketRegion ?? _providerFactory.DetectRegion(_stockCode);
            var marketProvider = region == MarketRegion.TW ? _marketDataProvider : _providerFactory.GetMarketDataProvider(region);
            var fundamentalProvider = region == MarketRegion.TW ? _fundamentalDataProvider : _providerFactory.GetFundamentalDataProvider(region);
            var chipProvider = _providerFactory.GetChipDataProvider(region);

            // US stocks don't have chip data
            var effectiveIncludeChip = _includeChip && chipProvider != null;

            // 1. Fetch data in parallel
            MarketData? priceData = null;
            MarketData? chipData = null;
            FundamentalData? fundamentalData = null;

            var tasks = new List<Task>();
            if (_includeTechnical || _includeFundamental)
            {
                tasks.Add(marketProvider.FetchAsync(_stockCode, cancellationToken)
                    .ContinueWith(t => priceData = t.Result, TaskScheduler.Default));
            }
            if (effectiveIncludeChip)
            {
                tasks.Add(FetchChipSafe(_stockCode, chipProvider!, cancellationToken)
                    .ContinueWith(t => chipData = t.Result, TaskScheduler.Default));
            }
            if (_includeFundamental)
            {
                tasks.Add(FetchFundamentalSafe(_stockCode, fundamentalProvider, cancellationToken)
                    .ContinueWith(t => fundamentalData = t.Result, TaskScheduler.Default));
            }
            await Task.WhenAll(tasks);

            // 2. Build context
            var context = new IndicatorContext
            {
                StockCode = _stockCode,
                Prices = priceData?.HistoricalPrices ?? new List<DailyPrice>(),
                Fundamentals = fundamentalData ?? priceData?.Fundamentals,
                Chips = chipData?.Chips
            };

            // 3. Calculate & filter indicators
            var allIndicators = _indicatorEngine.CalculateAll(context);
            var filtered = allIndicators
                .Where(i =>
                {
                    if (!_includeTechnical && i.Category == IndicatorCategory.Technical) return false;
                    if (!_includeChip && i.Category == IndicatorCategory.Chip) return false;
                    if (!_includeFundamental && i.Category == IndicatorCategory.Fundamental) return false;
                    return true;
                })
                .Where(i => _onlyIndicators.Count == 0 || _onlyIndicators.Contains(i.Name))
                .Where(i => !_excludeIndicators.Contains(i.Name))
                .ToList();

            // 4. Scoring
            StockScoreResponse? scoring = null;
            if (_includeScoring && filtered.Count > 0)
                scoring = BuildScoring(filtered);

            // 5. Risk
            RiskAssessment? risk = null;
            if (_includeRisk && filtered.Count > 0)
                risk = StockScoreService.AssessRisk(filtered, scoring?.CategoryScores ?? new List<CategoryScore>());

            return new StockAnalysisResult
            {
                StockCode = _stockCode,
                CompanyName = priceData?.CompanyName ?? chipData?.CompanyName ?? string.Empty,
                LatestClose = context.LatestClose,
                Indicators = filtered,
                Scoring = scoring,
                Risk = risk,
                DataRange = priceData?.HistoricalPrices.Count > 0
                    ? $"{priceData.HistoricalPrices.First().Date:yyyy-MM-dd} ~ {priceData.HistoricalPrices.Last().Date:yyyy-MM-dd}"
                    : "N/A",
                Configuration = new AnalysisConfiguration
                {
                    IncludeTechnical = _includeTechnical,
                    IncludeChip = effectiveIncludeChip,
                    IncludeFundamental = _includeFundamental,
                    IncludeScoring = _includeScoring,
                    IncludeRisk = _includeRisk,
                    OnlyIndicators = _onlyIndicators.ToList(),
                    ExcludeIndicators = _excludeIndicators.ToList()
                },
                GeneratedAt = DateTime.UtcNow
            };
        }

        private StockScoreResponse BuildScoring(List<IndicatorResult> indicators)
        {
            var effectiveConfig = new CategoryWeightConfig
            {
                Weights = new Dictionary<IndicatorCategory, decimal>(_weightConfig.Weights)
            };
            foreach (var (cat, w) in _customWeights)
                effectiveConfig.Weights[cat] = w;

            var grouped = indicators.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.ToList());
            var normalizedWeights = effectiveConfig.GetNormalizedWeights(grouped.Keys);

            var categoryScores = grouped
                .Select(kv => StockScoreService.ComputeCategoryScore(kv.Key, kv.Value, normalizedWeights))
                .ToList();

            var overallScore = categoryScores.Sum(cs => cs.WeightedScore);
            var overallDirection = StockScoreService.DetermineOverallDirection(overallScore);

            return new StockScoreResponse
            {
                StockCode = _stockCode,
                OverallScore = Math.Round(overallScore, 1),
                OverallDirection = overallDirection,
                Recommendation = StockScoreService.GenerateRecommendation(overallScore, overallDirection, categoryScores),
                CategoryScores = categoryScores,
                Indicators = indicators
            };
        }

        private async Task<MarketData?> FetchChipSafe(string stockCode, IChipDataProvider provider, CancellationToken ct)
        {
            try { return await provider.FetchAsync(stockCode, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chip data fetch failed for {stockCode}", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchFundamentalSafe(string stockCode, IFundamentalDataProvider provider, CancellationToken ct)
        {
            try { return await provider.FetchAsync(stockCode, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fundamental data fetch failed for {stockCode}", stockCode);
                return null;
            }
        }
    }
}
