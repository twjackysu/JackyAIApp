using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Scoring;

namespace JackyAIApp.Server.Tests.Services.Finance.Scoring
{
    public class StockScoreServiceTests
    {
        #region ComputeCategoryScore Tests

        [Fact]
        public void ComputeCategoryScore_CalculatesAverageCorrectly()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Score = 70, Direction = SignalDirection.Bullish },
                new() { Name = "MACD", Score = 60, Direction = SignalDirection.Bullish },
                new() { Name = "MA", Score = 50, Direction = SignalDirection.Neutral }
            };

            var weights = new Dictionary<IndicatorCategory, decimal>
            {
                [IndicatorCategory.Technical] = 0.5m,
                [IndicatorCategory.Chip] = 0.3m,
                [IndicatorCategory.Fundamental] = 0.2m
            };

            var result = StockScoreService.ComputeCategoryScore(
                IndicatorCategory.Technical, indicators, weights);

            Assert.Equal(60, result.Score); // (70+60+50)/3 = 60
            Assert.Equal(0.5m, result.Weight);
            Assert.Equal(30, result.WeightedScore); // 60 * 0.5 = 30
            Assert.Equal(3, result.IndicatorCount);
        }

        [Fact]
        public void ComputeCategoryScore_EmptyIndicators_Returns50()
        {
            var weights = new Dictionary<IndicatorCategory, decimal>
            {
                [IndicatorCategory.Technical] = 1m
            };

            var result = StockScoreService.ComputeCategoryScore(
                IndicatorCategory.Technical, new List<IndicatorResult>(), weights);

            Assert.Equal(50, result.Score);
            Assert.Equal(0, result.IndicatorCount);
        }

        [Fact]
        public void ComputeCategoryScore_HighScore_ReturnsBullishDirection()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Score = 80, Direction = SignalDirection.Bullish },
                new() { Name = "MACD", Score = 85, Direction = SignalDirection.StrongBullish }
            };

            var weights = new Dictionary<IndicatorCategory, decimal>
            {
                [IndicatorCategory.Technical] = 1m
            };

            var result = StockScoreService.ComputeCategoryScore(
                IndicatorCategory.Technical, indicators, weights);

            Assert.True(result.Score >= 80);
            Assert.Equal(SignalDirection.StrongBullish, result.Direction);
        }

        [Fact]
        public void ComputeCategoryScore_LowScore_ReturnsBearishDirection()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Score = 20, Direction = SignalDirection.Bearish },
                new() { Name = "MACD", Score = 15, Direction = SignalDirection.StrongBearish }
            };

            var weights = new Dictionary<IndicatorCategory, decimal>
            {
                [IndicatorCategory.Technical] = 1m
            };

            var result = StockScoreService.ComputeCategoryScore(
                IndicatorCategory.Technical, indicators, weights);

            Assert.True(result.Score < 20);
            Assert.Equal(SignalDirection.StrongBearish, result.Direction);
        }

        [Fact]
        public void ComputeCategoryScore_SummaryContainsCategoryName()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "MarginIndicator", Score = 50, Direction = SignalDirection.Neutral }
            };

            var weights = new Dictionary<IndicatorCategory, decimal>
            {
                [IndicatorCategory.Chip] = 1m
            };

            var result = StockScoreService.ComputeCategoryScore(
                IndicatorCategory.Chip, indicators, weights);

            Assert.Contains("籌碼面", result.Summary);
        }

        #endregion

        #region DetermineOverallDirection Tests

        [Theory]
        [InlineData(85, SignalDirection.StrongBullish)]
        [InlineData(80, SignalDirection.StrongBullish)]
        [InlineData(65, SignalDirection.Bullish)]
        [InlineData(60, SignalDirection.Bullish)]
        [InlineData(50, SignalDirection.Neutral)]
        [InlineData(40, SignalDirection.Neutral)]
        [InlineData(35, SignalDirection.Bearish)]
        [InlineData(20, SignalDirection.Bearish)]
        [InlineData(15, SignalDirection.StrongBearish)]
        [InlineData(0, SignalDirection.StrongBearish)]
        public void DetermineOverallDirection_MapsScoreCorrectly(decimal score, SignalDirection expected)
        {
            var result = StockScoreService.DetermineOverallDirection(score);
            Assert.Equal(expected, result);
        }

        #endregion

        #region GenerateRecommendation Tests

        [Fact]
        public void GenerateRecommendation_StrongBullish_ContainsPositiveAdvice()
        {
            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 85, Direction = SignalDirection.StrongBullish },
                new() { Category = IndicatorCategory.Chip, Score = 80, Direction = SignalDirection.StrongBullish }
            };

            var result = StockScoreService.GenerateRecommendation(
                85, SignalDirection.StrongBullish, categoryScores);

            Assert.Contains("看多", result);
        }

        [Fact]
        public void GenerateRecommendation_Bearish_ContainsNegativeAdvice()
        {
            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 25, Direction = SignalDirection.Bearish },
                new() { Category = IndicatorCategory.Chip, Score = 30, Direction = SignalDirection.Bearish }
            };

            var result = StockScoreService.GenerateRecommendation(
                25, SignalDirection.Bearish, categoryScores);

            Assert.Contains("偏空", result);
        }

        [Fact]
        public void GenerateRecommendation_TechBullishChipBearish_WarnsAboutDivergence()
        {
            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 70, Direction = SignalDirection.Bullish },
                new() { Category = IndicatorCategory.Chip, Score = 30, Direction = SignalDirection.Bearish }
            };

            var result = StockScoreService.GenerateRecommendation(
                50, SignalDirection.Neutral, categoryScores);

            Assert.Contains("出貨風險", result);
        }

        [Fact]
        public void GenerateRecommendation_TechBearishChipBullish_SuggestsAccumulation()
        {
            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 30, Direction = SignalDirection.Bearish },
                new() { Category = IndicatorCategory.Chip, Score = 70, Direction = SignalDirection.Bullish }
            };

            var result = StockScoreService.GenerateRecommendation(
                50, SignalDirection.Neutral, categoryScores);

            Assert.Contains("佈局", result);
        }

        #endregion

        #region AssessRisk Tests

        [Fact]
        public void AssessRisk_AllBullish_LowRisk()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Value = 60, Direction = SignalDirection.Bullish, Score = 65 },
                new() { Name = "MACD", Value = 1, Direction = SignalDirection.Bullish, Score = 65 },
                new() { Name = "MA", Value = 100, Direction = SignalDirection.Bullish, Score = 60 }
            };

            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 63 }
            };

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.Equal(RiskLevel.Low, result.Level);
            Assert.Empty(result.Factors);
        }

        [Fact]
        public void AssessRisk_HighDivergence_HigherRisk()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Value = 60, Direction = SignalDirection.Bullish, Score = 65 },
                new() { Name = "MACD", Value = 1, Direction = SignalDirection.Bearish, Score = 35 },
                new() { Name = "MA", Value = 100, Direction = SignalDirection.Bullish, Score = 60 },
                new() { Name = "KD", Value = 30, Direction = SignalDirection.Bearish, Score = 35 }
            };

            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 49 }
            };

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.True(result.DivergenceScore > 0);
        }

        [Fact]
        public void AssessRisk_ExtremeRSI_AddsFactor()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Value = 85, Direction = SignalDirection.Bullish, Score = 40,
                    SubValues = new Dictionary<string, decimal>() }
            };

            var categoryScores = new List<CategoryScore>();

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.Contains(result.Factors, f => f.Contains("超買"));
        }

        [Fact]
        public void AssessRisk_HighDirectorPledge_AddsFactor()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "DirectorPledge", Value = 45, Direction = SignalDirection.Bearish, Score = 30,
                    SubValues = new Dictionary<string, decimal>() }
            };

            var categoryScores = new List<CategoryScore>();

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.Contains(result.Factors, f => f.Contains("董監設質"));
        }

        [Fact]
        public void AssessRisk_ExtremeVolume_AddsFactor()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "VolumeRatio", Value = 2.5m, Direction = SignalDirection.Neutral, Score = 50,
                    SubValues = new Dictionary<string, decimal> { ["TodayVsAvg20"] = 4.0m } }
            };

            var categoryScores = new List<CategoryScore>();

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.Contains(result.Factors, f => f.Contains("量能異常"));
        }

        [Fact]
        public void AssessRisk_ManyFactors_VeryHighRisk()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Value = 85, Direction = SignalDirection.Bullish, Score = 40,
                    SubValues = new Dictionary<string, decimal>() },
                new() { Name = "DirectorPledge", Value = 50, Direction = SignalDirection.Bearish, Score = 30,
                    SubValues = new Dictionary<string, decimal>() },
                new() { Name = "VolumeRatio", Value = 3m, Direction = SignalDirection.Neutral, Score = 50,
                    SubValues = new Dictionary<string, decimal> { ["TodayVsAvg20"] = 5.0m } },
                new() { Name = "MACD", Value = -1, Direction = SignalDirection.Bearish, Score = 35,
                    SubValues = new Dictionary<string, decimal>() }
            };

            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 40 },
                new() { Category = IndicatorCategory.Chip, Score = 80 }
            };

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.True(result.Level >= RiskLevel.High);
            Assert.True(result.Factors.Count >= 3);
        }

        [Fact]
        public void AssessRisk_CategoryScoreDivergence_AddsFactor()
        {
            var indicators = new List<IndicatorResult>
            {
                new() { Name = "RSI", Value = 50, Direction = SignalDirection.Neutral, Score = 50,
                    SubValues = new Dictionary<string, decimal>() }
            };

            var categoryScores = new List<CategoryScore>
            {
                new() { Category = IndicatorCategory.Technical, Score = 80 },
                new() { Category = IndicatorCategory.Chip, Score = 30 }
            };

            var result = StockScoreService.AssessRisk(indicators, categoryScores);

            Assert.Contains(result.Factors, f => f.Contains("訊號不一致"));
        }

        #endregion

        #region CategoryWeightConfig Tests

        [Fact]
        public void CategoryWeightConfig_DefaultWeightsSumToOne()
        {
            var config = new CategoryWeightConfig();
            var sum = config.Weights.Values.Sum();
            Assert.Equal(1.0m, sum);
        }

        [Fact]
        public void CategoryWeightConfig_NormalizedWeights_RedistributesWhenCategoryMissing()
        {
            var config = new CategoryWeightConfig();

            // Only Technical and Chip available (no Fundamental)
            var normalized = config.GetNormalizedWeights(
                new[] { IndicatorCategory.Technical, IndicatorCategory.Chip });

            Assert.Equal(2, normalized.Count);
            Assert.False(normalized.ContainsKey(IndicatorCategory.Fundamental));

            // Weights should sum to 1
            var sum = normalized.Values.Sum();
            Assert.Equal(1.0m, Math.Round(sum, 10));

            // Technical should get proportionally more than Chip
            Assert.True(normalized[IndicatorCategory.Technical] > normalized[IndicatorCategory.Chip]);
        }

        [Fact]
        public void CategoryWeightConfig_NormalizedWeights_SingleCategory_Gets100Percent()
        {
            var config = new CategoryWeightConfig();

            var normalized = config.GetNormalizedWeights(
                new[] { IndicatorCategory.Technical });

            Assert.Single(normalized);
            Assert.Equal(1.0m, normalized[IndicatorCategory.Technical]);
        }

        [Fact]
        public void CategoryWeightConfig_NormalizedWeights_NoCategories_ReturnsEmpty()
        {
            var config = new CategoryWeightConfig();

            var normalized = config.GetNormalizedWeights(
                Array.Empty<IndicatorCategory>());

            Assert.Empty(normalized);
        }

        #endregion
    }
}
