using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class ForeignHoldingCalculatorTests
    {
        private readonly ForeignHoldingCalculator _calculator = new();

        private static IndicatorContext CreateContext(decimal holdingPct, decimal upperLimit = 100)
        {
            return new IndicatorContext
            {
                StockCode = "2330",
                Chips = new ChipData
                {
                    ForeignHoldingPercentage = holdingPct,
                    ForeignUpperLimit = upperLimit,
                    ForeignHoldingShares = 1000000
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
        public void CanCalculate_ReturnsFalse_WhenZeroPercentage()
        {
            var context = new IndicatorContext
            {
                StockCode = "2330",
                Chips = new ChipData { ForeignHoldingPercentage = 0 }
            };
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenHasData()
        {
            Assert.True(_calculator.CanCalculate(CreateContext(30)));
        }

        [Fact]
        public void Calculate_HighHolding_Bullish()
        {
            var result = _calculator.Calculate(CreateContext(55));
            Assert.Equal(SignalDirection.Bullish, result.Direction);
            Assert.True(result.Score >= 65);
        }

        [Fact]
        public void Calculate_VeryHighHolding_Neutral()
        {
            // >70% is so high that there's sell pressure risk
            var result = _calculator.Calculate(CreateContext(75));
            Assert.Equal(SignalDirection.Neutral, result.Direction);
        }

        [Fact]
        public void Calculate_LowHolding_Bearish()
        {
            var result = _calculator.Calculate(CreateContext(3));
            Assert.Equal(SignalDirection.Bearish, result.Direction);
            Assert.True(result.Score <= 40);
        }

        [Fact]
        public void Calculate_MediumHolding_Neutral()
        {
            var result = _calculator.Calculate(CreateContext(10));
            Assert.Equal(SignalDirection.Neutral, result.Direction);
        }

        [Fact]
        public void Calculate_SubValuesCorrect()
        {
            var result = _calculator.Calculate(CreateContext(45, 100));

            Assert.Equal(45m, result.SubValues["HoldingPercentage"]);
            Assert.Equal(100m, result.SubValues["UpperLimit"]);
            Assert.Equal(45m, result.SubValues["NearLimitRatio"]); // 45/100*100
        }

        [Fact]
        public void Calculate_ValueIsHoldingPercentage()
        {
            var result = _calculator.Calculate(CreateContext(33.5m));
            Assert.Equal(33.5m, result.Value);
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var result = _calculator.Calculate(CreateContext(30));

            Assert.Equal("ForeignHolding", result.Name);
            Assert.Equal(IndicatorCategory.Chip, result.Category);
        }
    }
}
