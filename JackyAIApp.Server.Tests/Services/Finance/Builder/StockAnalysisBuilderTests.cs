using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Builder;
using JackyAIApp.Server.Services.Finance.DataProviders;
using JackyAIApp.Server.Services.Finance.Indicators;
using JackyAIApp.Server.Services.Finance.Scoring;
using Microsoft.Extensions.Logging;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance.Builder
{
    public class StockAnalysisBuilderTests
    {
        private readonly Mock<IMarketDataProvider> _mockMarketDataProvider = new();
        private readonly Mock<IChipDataProvider> _mockChipDataProvider = new();
        private readonly Mock<IFundamentalDataProvider> _mockFundamentalDataProvider = new();
        private readonly Mock<IIndicatorEngine> _mockIndicatorEngine = new();
        private readonly CategoryWeightConfig _weightConfig = new();
        private readonly Mock<ILogger<StockAnalysisBuilder>> _mockLogger = new();

        private StockAnalysisBuilder CreateBuilder() => new(
            _mockMarketDataProvider.Object,
            _mockChipDataProvider.Object,
            _mockFundamentalDataProvider.Object,
            _mockIndicatorEngine.Object,
            _weightConfig,
            _mockLogger.Object);

        private MarketData CreateMockMarketData()
        {
            var prices = Enumerable.Range(1, 60).Select(i => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-60 + i),
                Open = 500 + i, High = 505 + i, Low = 495 + i, Close = 502 + i, Volume = 10000
            }).ToList();
            return new MarketData { StockCode = "2330", CompanyName = "台積電", HistoricalPrices = prices };
        }

        private List<IndicatorResult> CreateMockIndicators() => new()
        {
            new() { Name = "RSI", Category = IndicatorCategory.Technical, Score = 65, Direction = SignalDirection.Bullish, Value = 62, Signal = "偏多", Reason = "RSI=62" },
            new() { Name = "MACD", Category = IndicatorCategory.Technical, Score = 60, Direction = SignalDirection.Bullish, Value = 1.5m, Signal = "多頭", Reason = "MACD正值" },
            new() { Name = "MarginIndicator", Category = IndicatorCategory.Chip, Score = 55, Direction = SignalDirection.Neutral, Value = 0.5m, Signal = "中性", Reason = "融資餘額正常" },
        };

        private void SetupDefaults()
        {
            _mockMarketDataProvider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockMarketData());
            _mockChipDataProvider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MarketData { Chips = new ChipData() });
            _mockFundamentalDataProvider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundamentalData { PERatio = 20, PBRatio = 3.5m, DividendYield = 2.5m });
            _mockIndicatorEngine.Setup(e => e.CalculateAll(It.IsAny<IndicatorContext>()))
                .Returns(CreateMockIndicators());
        }

        [Fact]
        public async Task BuildAsync_ThrowsWhenNoStockCode()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => CreateBuilder().BuildAsync());
        }

        [Fact]
        public async Task BuildAsync_ReturnsResultWithAllCategories()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").BuildAsync();

            Assert.Equal("2330", result.StockCode);
            Assert.Equal("台積電", result.CompanyName);
            Assert.Equal(3, result.Indicators.Count);
            Assert.NotNull(result.Scoring);
            Assert.NotNull(result.Risk);
            Assert.True(result.Configuration.IncludeTechnical);
            Assert.True(result.Configuration.IncludeChip);
        }

        [Fact]
        public async Task BuildAsync_ExcludeChip_SkipsChipIndicators()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").WithChip(false).BuildAsync();

            Assert.DoesNotContain(result.Indicators, i => i.Category == IndicatorCategory.Chip);
            Assert.False(result.Configuration.IncludeChip);
        }

        [Fact]
        public async Task BuildAsync_OnlyIndicators_FiltersCorrectly()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").OnlyIndicators("RSI").BuildAsync();

            Assert.Single(result.Indicators);
            Assert.Equal("RSI", result.Indicators[0].Name);
        }

        [Fact]
        public async Task BuildAsync_ExcludeIndicators_FiltersCorrectly()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").ExcludeIndicators("MACD").BuildAsync();

            Assert.Equal(2, result.Indicators.Count);
            Assert.DoesNotContain(result.Indicators, i => i.Name == "MACD");
        }

        [Fact]
        public async Task BuildAsync_WithScoringDisabled_NoScoring()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").WithScoring(false).BuildAsync();
            Assert.Null(result.Scoring);
        }

        [Fact]
        public async Task BuildAsync_WithRiskDisabled_NoRisk()
        {
            SetupDefaults();
            var result = await CreateBuilder().ForStock("2330").WithRisk(false).BuildAsync();
            Assert.Null(result.Risk);
        }

        [Fact]
        public async Task BuildAsync_ChipDataFetchFails_ProceedsWithoutChip()
        {
            _mockMarketDataProvider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMockMarketData());
            _mockChipDataProvider.Setup(p => p.FetchAsync("2330", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("TWSE API error"));
            _mockIndicatorEngine.Setup(e => e.CalculateAll(It.IsAny<IndicatorContext>()))
                .Returns(new List<IndicatorResult>
                {
                    new() { Name = "RSI", Category = IndicatorCategory.Technical, Score = 65, Direction = SignalDirection.Bullish, Value = 62, Signal = "偏多", Reason = "test" }
                });

            var result = await CreateBuilder().ForStock("2330").BuildAsync();
            Assert.Equal("2330", result.StockCode);
            Assert.NotEmpty(result.Indicators);
        }

        [Fact]
        public async Task BuildAsync_CustomWeights_AppliedCorrectly()
        {
            SetupDefaults();
            var result = await CreateBuilder()
                .ForStock("2330")
                .WithCustomWeights(technical: 0.7m, chip: 0.3m)
                .BuildAsync();

            Assert.NotNull(result.Scoring);
            Assert.True(result.Scoring!.OverallScore > 0);
        }

        [Fact]
        public void Builder_FluentApi_Chainable()
        {
            var chained = CreateBuilder()
                .ForStock("2330")
                .WithTechnical(true).WithChip(false).WithFundamental(false)
                .WithScoring(true).WithRisk(true)
                .OnlyIndicators("RSI", "MACD").ExcludeIndicators("KD")
                .WithCustomWeights(technical: 0.8m);

            Assert.NotNull(chained);
        }
    }
}
