using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Market;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Services.Finance.Scoring
{
    /// <summary>
    /// Composite scoring service that aggregates all indicator categories
    /// into a single weighted score with risk assessment.
    /// </summary>
    public class StockScoreService : IStockScoreService
    {
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly IChipDataProvider _chipDataProvider;
        private readonly IFundamentalDataProvider _fundamentalDataProvider;
        private readonly IIndicatorEngine _indicatorEngine;
        private readonly CategoryWeightConfig _weightConfig;
        private readonly ILogger<StockScoreService> _logger;

        public StockScoreService(
            IMarketDataProvider marketDataProvider,
            IChipDataProvider chipDataProvider,
            IFundamentalDataProvider fundamentalDataProvider,
            IIndicatorEngine indicatorEngine,
            CategoryWeightConfig weightConfig,
            ILogger<StockScoreService> logger)
        {
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _chipDataProvider = chipDataProvider ?? throw new ArgumentNullException(nameof(chipDataProvider));
            _fundamentalDataProvider = fundamentalDataProvider ?? throw new ArgumentNullException(nameof(fundamentalDataProvider));
            _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
            _weightConfig = weightConfig ?? throw new ArgumentNullException(nameof(weightConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<StockScoreResponse> ScoreAsync(string stockCode, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Scoring stock {stockCode}", stockCode);

            // 1. Fetch all data in parallel
            var priceTask = _marketDataProvider.FetchAsync(stockCode, cancellationToken);
            var chipTask = FetchChipDataSafe(stockCode, cancellationToken);
            var fundamentalTask = FetchFundamentalDataSafe(stockCode, cancellationToken);

            await Task.WhenAll(priceTask, chipTask as Task, fundamentalTask as Task);

            var priceData = await priceTask;
            var chipData = await chipTask;
            var fundamentalData = await fundamentalTask;

            // 2. Build unified context
            var context = new IndicatorContext
            {
                StockCode = stockCode,
                Prices = priceData.HistoricalPrices,
                Fundamentals = fundamentalData ?? priceData.Fundamentals,
                Chips = chipData?.Chips
            };

            // 3. Calculate all indicators
            var allIndicators = _indicatorEngine.CalculateAll(context);

            // 4. Group by category and compute category scores
            var grouped = allIndicators
                .GroupBy(i => i.Category)
                .ToDictionary(g => g.Key, g => g.ToList());

            var availableCategories = grouped.Keys;
            var normalizedWeights = _weightConfig.GetNormalizedWeights(availableCategories);

            var categoryScores = new List<CategoryScore>();
            foreach (var (category, indicators) in grouped)
            {
                var categoryScore = ComputeCategoryScore(category, indicators, normalizedWeights);
                categoryScores.Add(categoryScore);
            }

            // 5. Compute overall score
            var overallScore = categoryScores.Sum(cs => cs.WeightedScore);
            var overallDirection = DetermineOverallDirection(overallScore);
            var recommendation = GenerateRecommendation(overallScore, overallDirection, categoryScores);

            // 6. Risk assessment
            var risk = AssessRisk(allIndicators, categoryScores);

            // 7. Build response
            var response = new StockScoreResponse
            {
                StockCode = stockCode,
                CompanyName = priceData.CompanyName,
                LatestClose = context.LatestClose,
                OverallScore = Math.Round(overallScore, 1),
                OverallDirection = overallDirection,
                Recommendation = recommendation,
                CategoryScores = categoryScores,
                Indicators = allIndicators,
                Risk = risk,
                DataRange = priceData.HistoricalPrices.Count > 0
                    ? $"{priceData.HistoricalPrices.First().Date:yyyy-MM-dd} ~ {priceData.HistoricalPrices.Last().Date:yyyy-MM-dd}"
                    : "N/A",
                GeneratedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Stock {stockCode} scored {score} ({direction}), risk={risk}",
                stockCode, response.OverallScore, response.OverallDirection, response.Risk.Level);

            return response;
        }

        /// <summary>
        /// Fetches chip data safely; returns null if unavailable.
        /// </summary>
        private async Task<MarketData?> FetchChipDataSafe(string stockCode, CancellationToken cancellationToken)
        {
            try
            {
                return await _chipDataProvider.FetchAsync(stockCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch chip data for {stockCode}, proceeding without it", stockCode);
                return null;
            }
        }

        private async Task<FundamentalData?> FetchFundamentalDataSafe(string stockCode, CancellationToken cancellationToken)
        {
            try
            {
                return await _fundamentalDataProvider.FetchAsync(stockCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch fundamental data for {stockCode}, proceeding without it", stockCode);
                return null;
            }
        }

        /// <summary>
        /// Computes a single category's weighted average score.
        /// </summary>
        internal static CategoryScore ComputeCategoryScore(
            IndicatorCategory category,
            List<IndicatorResult> indicators,
            Dictionary<IndicatorCategory, decimal> weights)
        {
            var avgScore = indicators.Count > 0
                ? indicators.Average(i => (decimal)i.Score)
                : 50m;

            var weight = weights.GetValueOrDefault(category, 0m);
            var weightedScore = avgScore * weight;
            var direction = DetermineOverallDirection(avgScore);

            var summary = GenerateCategorySummary(category, indicators, direction);

            return new CategoryScore
            {
                Category = category,
                Score = Math.Round(avgScore, 1),
                Weight = weight,
                WeightedScore = Math.Round(weightedScore, 1),
                Direction = direction,
                Summary = summary,
                IndicatorCount = indicators.Count
            };
        }

        /// <summary>
        /// Maps a numeric score to a signal direction.
        /// </summary>
        internal static SignalDirection DetermineOverallDirection(decimal score)
        {
            return score switch
            {
                >= 80 => SignalDirection.StrongBullish,
                >= 60 => SignalDirection.Bullish,
                >= 40 => SignalDirection.Neutral,
                >= 20 => SignalDirection.Bearish,
                _ => SignalDirection.StrongBearish
            };
        }

        /// <summary>
        /// Generates a human-readable recommendation based on the overall score.
        /// </summary>
        internal static string GenerateRecommendation(
            decimal overallScore,
            SignalDirection direction,
            List<CategoryScore> categoryScores)
        {
            var techScore = categoryScores.FirstOrDefault(c => c.Category == IndicatorCategory.Technical);
            var chipScore = categoryScores.FirstOrDefault(c => c.Category == IndicatorCategory.Chip);

            var baseRecommendation = direction switch
            {
                SignalDirection.StrongBullish => "強烈看多：技術面與籌碼面均呈現積極訊號，可考慮積極布局。",
                SignalDirection.Bullish => "偏多操作：整體指標偏向正面，可考慮逢低佈局或持續持有。",
                SignalDirection.Neutral => "中性觀望：多空訊號交錯，建議觀望或小量試單。",
                SignalDirection.Bearish => "偏空操作：整體指標偏向負面，建議減碼或觀望。",
                SignalDirection.StrongBearish => "強烈看空：多項指標發出警訊，建議避開或停損。",
                _ => "資料不足，無法給出明確建議。"
            };

            // Add nuance based on category divergence
            if (techScore != null && chipScore != null)
            {
                var techDir = techScore.Direction;
                var chipDir = chipScore.Direction;

                if (IsBullish(techDir) && IsBearish(chipDir))
                {
                    baseRecommendation += " ⚠️ 注意：技術面看多但籌碼面偏空，可能存在出貨風險。";
                }
                else if (IsBearish(techDir) && IsBullish(chipDir))
                {
                    baseRecommendation += " 💡 提示：技術面偏弱但籌碼面看多，主力可能正在佈局。";
                }
            }

            return baseRecommendation;
        }

        /// <summary>
        /// Assesses investment risk based on indicator divergence and extreme values.
        /// </summary>
        internal static RiskAssessment AssessRisk(
            List<IndicatorResult> indicators,
            List<CategoryScore> categoryScores)
        {
            var factors = new List<string>();

            // 1. Check indicator divergence (conflicting signals)
            var bullishCount = indicators.Count(i => IsBullish(i.Direction));
            var bearishCount = indicators.Count(i => IsBearish(i.Direction));
            var total = indicators.Count;

            decimal divergenceScore = 0;
            if (total > 0)
            {
                // Max divergence when 50/50 split between bullish and bearish
                var minSide = Math.Min(bullishCount, bearishCount);
                divergenceScore = total > 0 ? (decimal)minSide / total * 200 : 0;
            }

            if (divergenceScore > 60)
            {
                factors.Add($"指標嚴重分歧：{bullishCount}個看多 vs {bearishCount}個看空");
            }
            else if (divergenceScore > 30)
            {
                factors.Add($"指標存在分歧：{bullishCount}個看多 vs {bearishCount}個看空");
            }

            // 2. Check for extreme RSI
            var rsi = indicators.FirstOrDefault(i => i.Name == "RSI");
            if (rsi != null)
            {
                if (rsi.Value > 80) factors.Add($"RSI={rsi.Value:F1}，嚴重超買");
                else if (rsi.Value < 20) factors.Add($"RSI={rsi.Value:F1}，嚴重超賣");
            }

            // 3. Check for extreme volume
            var vol = indicators.FirstOrDefault(i => i.Name == "VolumeRatio");
            if (vol != null && vol.SubValues.TryGetValue("TodayVsAvg20", out var todayVsAvg))
            {
                if (todayVsAvg > 3) factors.Add($"今日成交量為20日均量的{todayVsAvg:F1}倍，量能異常");
            }

            // 4. Check for high director pledge ratio
            var pledge = indicators.FirstOrDefault(i => i.Name == "DirectorPledge");
            if (pledge != null && pledge.Value > 30)
            {
                factors.Add($"董監設質比率={pledge.Value:F1}%，偏高");
            }

            // 5. Check category score divergence
            if (categoryScores.Count >= 2)
            {
                var maxCat = categoryScores.Max(c => c.Score);
                var minCat = categoryScores.Min(c => c.Score);
                if (maxCat - minCat > 30)
                {
                    factors.Add("各面向分數差距大，訊號不一致");
                }
            }

            // 6. Determine risk level
            var riskLevel = (factors.Count, divergenceScore) switch
            {
                ( >= 4, _) => RiskLevel.VeryHigh,
                ( >= 3, _) => RiskLevel.High,
                (_, > 50) => RiskLevel.High,
                ( >= 1, _) => RiskLevel.Medium,
                _ => RiskLevel.Low
            };

            return new RiskAssessment
            {
                Level = riskLevel,
                Factors = factors,
                DivergenceScore = Math.Round(divergenceScore, 1)
            };
        }

        /// <summary>
        /// Generates a summary for a category.
        /// </summary>
        private static string GenerateCategorySummary(
            IndicatorCategory category,
            List<IndicatorResult> indicators,
            SignalDirection direction)
        {
            var categoryName = category switch
            {
                IndicatorCategory.Technical => "技術面",
                IndicatorCategory.Chip => "籌碼面",
                IndicatorCategory.Fundamental => "基本面",
                _ => category.ToString()
            };

            var directionText = direction switch
            {
                SignalDirection.StrongBullish => "強勢看多",
                SignalDirection.Bullish => "偏多",
                SignalDirection.Neutral => "中性",
                SignalDirection.Bearish => "偏空",
                SignalDirection.StrongBearish => "強勢看空",
                _ => "未知"
            };

            var bullish = indicators.Count(i => IsBullish(i.Direction));
            var bearish = indicators.Count(i => IsBearish(i.Direction));
            var neutral = indicators.Count(i => i.Direction == SignalDirection.Neutral);

            return $"{categoryName}{directionText}（{bullish}多/{neutral}中/{bearish}空，共{indicators.Count}項指標）";
        }

        private static bool IsBullish(SignalDirection d) =>
            d is SignalDirection.Bullish or SignalDirection.StrongBullish;

        private static bool IsBearish(SignalDirection d) =>
            d is SignalDirection.Bearish or SignalDirection.StrongBearish;
    }
}
