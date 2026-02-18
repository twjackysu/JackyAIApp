using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class KDCalculatorTests
    {
        private readonly KDCalculator _calculator = new();

        private static IndicatorContext CreateContext(List<DailyPrice> prices)
        {
            return new IndicatorContext { StockCode = "2330", Prices = prices };
        }

        private static List<DailyPrice> CreatePricesWithHighLow(
            decimal[] closes, decimal[] highs, decimal[] lows)
        {
            return closes.Select((c, i) => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-closes.Length + i + 1),
                Open = c,
                High = highs[i],
                Low = lows[i],
                Close = c,
                Volume = 1000
            }).ToList();
        }

        private static List<DailyPrice> CreateSimplePrices(decimal[] closes)
        {
            return closes.Select((c, i) => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-closes.Length + i + 1),
                Open = c,
                High = c + 2,
                Low = c - 2,
                Close = c,
                Volume = 1000
            }).ToList();
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenInsufficientData()
        {
            var prices = CreateSimplePrices(new decimal[] { 100, 101, 102 });
            Assert.False(_calculator.CanCalculate(CreateContext(prices)));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenEnoughData()
        {
            // Need at least 9 + 3 + 3 = 15 data points
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)(100 + i)).ToArray();
            var prices = CreateSimplePrices(closes);
            Assert.True(_calculator.CanCalculate(CreateContext(prices)));
        }

        [Fact]
        public void Calculate_KDBetween0And100()
        {
            var closes = Enumerable.Range(1, 30)
                .Select(i => 100m + (decimal)Math.Sin(i * 0.5) * 10)
                .ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.InRange(result.SubValues["K"], 0, 100);
            Assert.InRange(result.SubValues["D"], 0, 100);
        }

        [Fact]
        public void Calculate_HighKD_InUptrend()
        {
            // Strong uptrend → K and D should be high
            var closes = Enumerable.Range(1, 30).Select(i => 50m + i * 3).ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.True(result.SubValues["K"] > 50, $"K should be > 50 in uptrend, got {result.SubValues["K"]}");
        }

        [Fact]
        public void Calculate_LowKD_InDowntrend()
        {
            // Strong downtrend → K and D should be low
            var closes = Enumerable.Range(1, 30).Select(i => 200m - i * 3).ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.True(result.SubValues["K"] < 50, $"K should be < 50 in downtrend, got {result.SubValues["K"]}");
        }

        [Fact]
        public void Calculate_ReturnsRSV()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)(100 + i)).ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.True(result.SubValues.ContainsKey("RSV"));
            Assert.InRange(result.SubValues["RSV"], 0, 100);
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)(100 + i)).ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.Equal("KD", result.Name);
            Assert.Equal(IndicatorCategory.Technical, result.Category);
        }

        [Fact]
        public void Calculate_ReasonContainsKDValues()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)(100 + i)).ToArray();
            var prices = CreateSimplePrices(closes);

            var result = _calculator.Calculate(CreateContext(prices));

            Assert.Contains("K=", result.Reason);
            Assert.Contains("D=", result.Reason);
        }
    }
}
