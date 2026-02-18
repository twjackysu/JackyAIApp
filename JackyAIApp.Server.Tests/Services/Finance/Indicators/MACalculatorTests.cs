using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class MACalculatorTests
    {
        private readonly MACalculator _calculator = new();

        private static IndicatorContext CreateContext(decimal[] closes)
        {
            var prices = closes.Select((c, i) => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-closes.Length + i + 1),
                Open = c,
                High = c + 1,
                Low = c - 1,
                Close = c,
                Volume = 1000
            }).ToList();

            return new IndicatorContext { StockCode = "2330", Prices = prices };
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenInsufficientData()
        {
            var context = CreateContext(new decimal[] { 100, 101, 102 });
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenEnoughData()
        {
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);
            Assert.True(_calculator.CanCalculate(context));
        }

        [Fact]
        public void Calculate_ReturnsCorrectMA5()
        {
            // 20 data points, last 5: 16, 17, 18, 19, 20 → MA5 = 18
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Equal("MA", result.Name);
            Assert.Equal(IndicatorCategory.Technical, result.Category);
            Assert.Equal(18m, result.SubValues["MA5"]);
        }

        [Fact]
        public void Calculate_ReturnsCorrectMA20()
        {
            // 20 data points: 1..20 → MA20 = 10.5
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Equal(10.5m, result.SubValues["MA20"]);
        }

        [Fact]
        public void Calculate_IncludesMA60_WhenEnoughData()
        {
            var closes = Enumerable.Range(1, 65).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.SubValues.ContainsKey("MA60"));
        }

        [Fact]
        public void Calculate_ExcludesMA60_WhenInsufficientData()
        {
            var closes = Enumerable.Range(1, 30).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.False(result.SubValues.ContainsKey("MA60"));
        }

        [Fact]
        public void Calculate_DetectsBullishAlignment()
        {
            // Uptrend: Close > MA5 > MA20 > MA60
            var closes = Enumerable.Range(1, 65).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Equal(SignalDirection.StrongBullish, result.Direction);
            Assert.Contains("多頭", result.Signal);
            Assert.True(result.Score >= 80);
        }

        [Fact]
        public void Calculate_DetectsBearishAlignment()
        {
            // Downtrend: Close < MA5 < MA20 < MA60
            var closes = Enumerable.Range(1, 65).Select(i => 65m - i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Equal(SignalDirection.StrongBearish, result.Direction);
            Assert.Contains("空頭", result.Signal);
            Assert.True(result.Score <= 20);
        }

        [Fact]
        public void Calculate_ReasonContainsPriceInfo()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Contains("MA5=", result.Reason);
            Assert.Contains("MA20=", result.Reason);
            Assert.Contains("收盤價", result.Reason);
        }
    }
}
