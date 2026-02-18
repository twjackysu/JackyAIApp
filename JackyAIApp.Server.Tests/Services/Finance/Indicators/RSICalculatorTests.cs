using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class RSICalculatorTests
    {
        private readonly RSICalculator _calculator = new();

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
        public void CanCalculate_ReturnsTrue_When15DataPoints()
        {
            var closes = Enumerable.Range(1, 16).Select(i => (decimal)(100 + i)).ToArray();
            Assert.True(_calculator.CanCalculate(CreateContext(closes)));
        }

        [Fact]
        public void Calculate_RSIBetween0And100()
        {
            var closes = Enumerable.Range(1, 30)
                .Select(i => 100m + (i % 3 == 0 ? -2m : 1.5m))
                .ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.InRange(result.Value, 0, 100);
        }

        [Fact]
        public void Calculate_RSINear100_WhenAllGains()
        {
            // Continuous uptrend → RSI should be very high
            var closes = Enumerable.Range(1, 30).Select(i => 100m + i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.Value > 90, $"Expected RSI > 90 for pure uptrend, got {result.Value}");
        }

        [Fact]
        public void Calculate_RSINear0_WhenAllLosses()
        {
            // Continuous downtrend → RSI should be very low
            var closes = Enumerable.Range(1, 30).Select(i => 100m - i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.Value < 10, $"Expected RSI < 10 for pure downtrend, got {result.Value}");
        }

        [Fact]
        public void Calculate_DetectsOverbought()
        {
            var closes = Enumerable.Range(1, 30).Select(i => 100m + i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Contains("超買", result.Signal);
        }

        [Fact]
        public void Calculate_DetectsOversold()
        {
            var closes = Enumerable.Range(1, 30).Select(i => 100m - i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.Contains("超賣", result.Signal);
        }

        [Fact]
        public void Calculate_ScoreHighForOversold()
        {
            // Oversold = potential buy opportunity → higher score
            var closes = Enumerable.Range(1, 30).Select(i => 100m - i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.Score >= 70);
        }

        [Fact]
        public void Calculate_ScoreLowForOverbought()
        {
            // Overbought = caution → lower score
            var closes = Enumerable.Range(1, 30).Select(i => 100m + i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.Score <= 30);
        }

        [Fact]
        public void Calculate_RSINear50_WhenEqualGainsAndLosses()
        {
            // Alternating +1, -1 → RSI should be near 50
            var closes = new decimal[30];
            closes[0] = 100;
            for (int i = 1; i < 30; i++)
                closes[i] = closes[i - 1] + (i % 2 == 0 ? 1m : -1m);

            var context = CreateContext(closes);
            var result = _calculator.Calculate(context);

            Assert.InRange(result.Value, 40, 60);
        }
    }
}
