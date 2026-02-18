using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;
using Microsoft.Extensions.Logging;
using Moq;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class IndicatorEngineTests
    {
        private readonly IndicatorEngine _engine;

        public IndicatorEngineTests()
        {
            var calculators = new IIndicatorCalculator[]
            {
                new MACalculator(),
                new RSICalculator(),
                new MACDCalculator(),
                new KDCalculator(),
                new VolumeRatioCalculator(),
                new BollingerBandCalculator(),
                new MarginIndicatorCalculator(),
                new ForeignHoldingCalculator(),
                new DirectorPledgeCalculator()
            };

            var logger = new Mock<ILogger<IndicatorEngine>>();
            _engine = new IndicatorEngine(calculators, logger.Object);
        }

        private static IndicatorContext CreateFullContext()
        {
            var prices = Enumerable.Range(1, 70).Select(i => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-70 + i),
                Open = 100m + i * 0.5m,
                High = 102m + i * 0.5m,
                Low = 98m + i * 0.5m,
                Close = 100m + i * 0.5m,
                Volume = 5000 + i * 100
            }).ToList();

            return new IndicatorContext
            {
                StockCode = "2330",
                Prices = prices,
                Chips = new ChipData
                {
                    MarginBalance = 5000,
                    MarginPreviousBalance = 4800,
                    MarginLimit = 10000,
                    ShortBalance = 200,
                    ShortPreviousBalance = 180,
                    ForeignHoldingPercentage = 45,
                    ForeignUpperLimit = 100,
                    ForeignHoldingShares = 1000000,
                    DirectorHoldings = new List<DirectorHolding>
                    {
                        new() { Title = "董事長", Name = "Someone", CurrentShares = 50000, PledgedShares = 0, PledgeRatio = "0%" }
                    },
                    TotalDirectorShares = 50000,
                    TotalDirectorPledged = 0,
                    DirectorPledgeRatio = 0
                }
            };
        }

        [Fact]
        public void CalculateAll_ReturnsAllApplicableIndicators()
        {
            var context = CreateFullContext();
            var results = _engine.CalculateAll(context);

            // Should include both technical and chip indicators
            Assert.True(results.Count >= 8, $"Expected at least 8 indicators, got {results.Count}");

            var names = results.Select(r => r.Name).ToHashSet();
            Assert.Contains("MA", names);
            Assert.Contains("RSI", names);
            Assert.Contains("MACD", names);
            Assert.Contains("KD", names);
            Assert.Contains("VolumeRatio", names);
            Assert.Contains("BollingerBands", names);
            Assert.Contains("MarginTrading", names);
            Assert.Contains("ForeignHolding", names);
            Assert.Contains("DirectorPledge", names);
        }

        [Fact]
        public void CalculateByCategory_ReturnsOnlyTechnical()
        {
            var context = CreateFullContext();
            var results = _engine.CalculateByCategory(context, IndicatorCategory.Technical);

            Assert.True(results.All(r => r.Category == IndicatorCategory.Technical));
            Assert.True(results.Count >= 5);
        }

        [Fact]
        public void CalculateByCategory_ReturnsOnlyChip()
        {
            var context = CreateFullContext();
            var results = _engine.CalculateByCategory(context, IndicatorCategory.Chip);

            Assert.True(results.All(r => r.Category == IndicatorCategory.Chip));
            Assert.True(results.Count >= 3);
        }

        [Fact]
        public void CalculateByName_ReturnsSpecificIndicator()
        {
            var context = CreateFullContext();
            var result = _engine.CalculateByName(context, "RSI");

            Assert.NotNull(result);
            Assert.Equal("RSI", result!.Name);
        }

        [Fact]
        public void CalculateByName_ReturnsNull_WhenNotFound()
        {
            var context = CreateFullContext();
            var result = _engine.CalculateByName(context, "NonExistent");

            Assert.Null(result);
        }

        [Fact]
        public void CalculateAll_SkipsIndicators_WhenInsufficientData()
        {
            // Only 5 data points → most technical indicators can't run
            var context = new IndicatorContext
            {
                StockCode = "2330",
                Prices = Enumerable.Range(1, 5).Select(i => new DailyPrice
                {
                    Date = DateTime.Today.AddDays(-5 + i),
                    Close = 100 + i,
                    High = 102 + i,
                    Low = 98 + i,
                    Volume = 5000
                }).ToList()
            };

            var results = _engine.CalculateAll(context);

            // Should return 0 since no indicator has enough data
            Assert.Empty(results);
        }

        [Fact]
        public void CalculateAll_AllScoresWithinRange()
        {
            var context = CreateFullContext();
            var results = _engine.CalculateAll(context);

            foreach (var result in results)
            {
                Assert.InRange(result.Score, 0, 100);
                Assert.False(string.IsNullOrEmpty(result.Signal), $"Signal should not be empty for {result.Name}");
                Assert.False(string.IsNullOrEmpty(result.Reason), $"Reason should not be empty for {result.Name}");
            }
        }

        [Fact]
        public void CalculateAll_DoesNotThrow_WhenChipDataMissing()
        {
            var context = new IndicatorContext
            {
                StockCode = "2330",
                Prices = Enumerable.Range(1, 70).Select(i => new DailyPrice
                {
                    Date = DateTime.Today.AddDays(-70 + i),
                    Close = 100 + i,
                    High = 102 + i,
                    Low = 98 + i,
                    Volume = 5000
                }).ToList()
                // No Chips data
            };

            var results = _engine.CalculateAll(context);

            // Should only return technical indicators, no chip indicators
            Assert.True(results.All(r => r.Category == IndicatorCategory.Technical));
        }
    }
}
