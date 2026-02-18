using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class DirectorPledgeCalculatorTests
    {
        private readonly DirectorPledgeCalculator _calculator = new();

        private static IndicatorContext CreateContext(decimal pledgeRatio, int directorCount = 5)
        {
            var holdings = Enumerable.Range(1, directorCount).Select(i => new DirectorHolding
            {
                Title = $"董事{i}",
                Name = $"Person{i}",
                CurrentShares = 10000,
                PledgedShares = (long)(10000 * pledgeRatio / 100),
                PledgeRatio = $"{pledgeRatio:F2}%"
            }).ToList();

            return new IndicatorContext
            {
                StockCode = "2330",
                Chips = new ChipData
                {
                    DirectorHoldings = holdings,
                    TotalDirectorShares = directorCount * 10000,
                    TotalDirectorPledged = (long)(directorCount * 10000 * pledgeRatio / 100),
                    DirectorPledgeRatio = pledgeRatio
                }
            };
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenNoChipData()
        {
            var context = new IndicatorContext { StockCode = "2330" };
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenNoDirectors()
        {
            var context = new IndicatorContext
            {
                StockCode = "2330",
                Chips = new ChipData { DirectorHoldings = new List<DirectorHolding>() }
            };
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenHasDirectors()
        {
            Assert.True(_calculator.CanCalculate(CreateContext(10)));
        }

        [Fact]
        public void Calculate_ZeroPledge_StrongBullish()
        {
            var result = _calculator.Calculate(CreateContext(0));

            Assert.Equal(SignalDirection.Bullish, result.Direction);
            Assert.True(result.Score >= 70);
            Assert.Contains("零設質", result.Signal);
        }

        [Fact]
        public void Calculate_LowPledge_Healthy()
        {
            var result = _calculator.Calculate(CreateContext(3));

            Assert.True(result.Score >= 65);
            Assert.Contains("極低", result.Signal);
        }

        [Fact]
        public void Calculate_MediumPledge_Neutral()
        {
            var result = _calculator.Calculate(CreateContext(20));

            Assert.Equal(SignalDirection.Neutral, result.Direction);
        }

        [Fact]
        public void Calculate_HighPledge_Bearish()
        {
            var result = _calculator.Calculate(CreateContext(40));

            Assert.Equal(SignalDirection.Bearish, result.Direction);
            Assert.True(result.Score <= 30);
            Assert.Contains("偏高", result.Signal);
        }

        [Fact]
        public void Calculate_VeryHighPledge_StrongBearish()
        {
            var result = _calculator.Calculate(CreateContext(60));

            Assert.Equal(SignalDirection.StrongBearish, result.Direction);
            Assert.True(result.Score <= 15);
            Assert.Contains("斷頭", result.Signal);
        }

        [Fact]
        public void Calculate_SubValuesCorrect()
        {
            var result = _calculator.Calculate(CreateContext(25, 3));

            Assert.Equal(25m, result.SubValues["PledgeRatio"]);
            Assert.Equal(3, result.SubValues["DirectorCount"]);
            Assert.Equal(30000m, result.SubValues["TotalDirectorShares"]); // 3 * 10000
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var result = _calculator.Calculate(CreateContext(10));

            Assert.Equal("DirectorPledge", result.Name);
            Assert.Equal(IndicatorCategory.Chip, result.Category);
        }
    }
}
