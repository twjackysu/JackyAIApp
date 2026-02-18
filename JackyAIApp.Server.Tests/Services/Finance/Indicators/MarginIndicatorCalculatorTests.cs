using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class MarginIndicatorCalculatorTests
    {
        private readonly MarginIndicatorCalculator _calculator = new();

        private static IndicatorContext CreateContext(ChipData chips)
        {
            return new IndicatorContext { StockCode = "2330", Chips = chips };
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenNoChipData()
        {
            var context = new IndicatorContext { StockCode = "2330" };
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenMarginBalanceIsZero()
        {
            var context = CreateContext(new ChipData { MarginBalance = 0 });
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenMarginBalanceExists()
        {
            var context = CreateContext(new ChipData { MarginBalance = 5000 });
            Assert.True(_calculator.CanCalculate(context));
        }

        [Fact]
        public void Calculate_HighUtilization_ReturnsBearish()
        {
            var chips = new ChipData
            {
                MarginBalance = 9000,
                MarginPreviousBalance = 8900,
                MarginLimit = 10000,
                ShortBalance = 100,
                ShortPreviousBalance = 100
            };

            var result = _calculator.Calculate(CreateContext(chips));

            // 90% utilization → should be strongly bearish
            Assert.True(result.Score <= 30);
            Assert.Contains("融資使用率", result.Signal);
        }

        [Fact]
        public void Calculate_LowUtilization_ReturnsBullish()
        {
            var chips = new ChipData
            {
                MarginBalance = 1000,
                MarginPreviousBalance = 1000,
                MarginLimit = 10000,
                ShortBalance = 100,
                ShortPreviousBalance = 100
            };

            var result = _calculator.Calculate(CreateContext(chips));

            Assert.True(result.Score >= 60);
            Assert.Contains("健康", result.Signal);
        }

        [Fact]
        public void Calculate_HighShortMarginRatio_ReturnsBullish()
        {
            // 券資比 > 30% → 軋空動能
            var chips = new ChipData
            {
                MarginBalance = 1000,
                MarginPreviousBalance = 1000,
                MarginLimit = 10000,
                ShortBalance = 400, // 40% ratio
                ShortPreviousBalance = 400
            };

            var result = _calculator.Calculate(CreateContext(chips));

            Assert.Contains("軋空", result.Signal);
            Assert.True(result.Score >= 70);
        }

        [Fact]
        public void Calculate_MarginSurge_ReturnsBearish()
        {
            // Margin balance increased by >1000 → retail chasing
            var chips = new ChipData
            {
                MarginBalance = 5000,
                MarginPreviousBalance = 3000,
                MarginLimit = 20000,
                ShortBalance = 100,
                ShortPreviousBalance = 100
            };

            var result = _calculator.Calculate(CreateContext(chips));

            Assert.Contains("融資大幅增加", result.Signal);
            Assert.True(result.Score <= 40);
        }

        [Fact]
        public void Calculate_MarginDrop_ReturnsBullish()
        {
            // Margin balance decreased by >1000 → 籌碼沉澱
            var chips = new ChipData
            {
                MarginBalance = 3000,
                MarginPreviousBalance = 5000,
                MarginLimit = 20000,
                ShortBalance = 100,
                ShortPreviousBalance = 100
            };

            var result = _calculator.Calculate(CreateContext(chips));

            Assert.Contains("融資大幅減少", result.Signal);
            Assert.True(result.Score >= 65);
        }

        [Fact]
        public void Calculate_SubValuesCorrect()
        {
            var chips = new ChipData
            {
                MarginBalance = 5000,
                MarginPreviousBalance = 4800,
                MarginLimit = 10000,
                ShortBalance = 500,
                ShortPreviousBalance = 450,
                OffsetVolume = 100
            };

            var result = _calculator.Calculate(CreateContext(chips));

            Assert.Equal(5000m, result.SubValues["MarginBalance"]);
            Assert.Equal(200m, result.SubValues["MarginChange"]); // 5000 - 4800
            Assert.Equal(50m, result.SubValues["MarginUtilization"]); // 5000/10000*100
            Assert.Equal(10m, result.SubValues["ShortMarginRatio"]); // 500/5000*100
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var chips = new ChipData { MarginBalance = 5000, MarginLimit = 10000 };
            var result = _calculator.Calculate(CreateContext(chips));

            Assert.Equal("MarginTrading", result.Name);
            Assert.Equal(IndicatorCategory.Chip, result.Category);
        }
    }
}
