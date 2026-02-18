using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class MACDCalculatorTests
    {
        private readonly MACDCalculator _calculator = new();

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
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();
            Assert.False(_calculator.CanCalculate(CreateContext(closes)));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenEnoughData()
        {
            // Need at least 26 + 9 = 35 data points
            var closes = Enumerable.Range(1, 40).Select(i => (decimal)(100 + i)).ToArray();
            Assert.True(_calculator.CanCalculate(CreateContext(closes)));
        }

        [Fact]
        public void Calculate_ReturnsAllSubValues()
        {
            var closes = Enumerable.Range(1, 50).Select(i => 100m + i).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.SubValues.ContainsKey("MACD"));
            Assert.True(result.SubValues.ContainsKey("Signal"));
            Assert.True(result.SubValues.ContainsKey("Histogram"));
            Assert.True(result.SubValues.ContainsKey("DIF"));
        }

        [Fact]
        public void Calculate_HistogramEqualsMACD_MinusSignal()
        {
            var closes = Enumerable.Range(1, 50)
                .Select(i => 100m + i + (decimal)Math.Sin(i * 0.3) * 5)
                .ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            var expected = result.SubValues["MACD"] - result.SubValues["Signal"];
            Assert.Equal(expected, result.SubValues["Histogram"], 4);
        }

        [Fact]
        public void Calculate_PositiveMACD_InUptrend()
        {
            // Strong uptrend → MACD should be positive
            var closes = Enumerable.Range(1, 50).Select(i => 50m + i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.SubValues["MACD"] > 0, "MACD should be positive in uptrend");
        }

        [Fact]
        public void Calculate_NegativeMACD_InDowntrend()
        {
            // Strong downtrend → MACD should be negative
            var closes = Enumerable.Range(1, 50).Select(i => 200m - i * 2).ToArray();
            var context = CreateContext(closes);

            var result = _calculator.Calculate(context);

            Assert.True(result.SubValues["MACD"] < 0, "MACD should be negative in downtrend");
        }

        [Fact]
        public void Calculate_DetectsGoldenCross()
        {
            // Create data that transitions from downtrend to uptrend
            var closes = new decimal[50];
            for (int i = 0; i < 30; i++) closes[i] = 100m - i * 0.5m; // downtrend
            for (int i = 30; i < 50; i++) closes[i] = closes[29] + (i - 29) * 2m; // strong uptrend

            var context = CreateContext(closes);
            var result = _calculator.Calculate(context);

            // After transition, should detect bullish signal
            Assert.True(result.Direction == SignalDirection.StrongBullish ||
                         result.Direction == SignalDirection.Bullish,
                         $"Expected bullish direction after trend reversal, got {result.Direction}");
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var closes = Enumerable.Range(1, 50).Select(i => (decimal)(100 + i)).ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Equal("MACD", result.Name);
            Assert.Equal(IndicatorCategory.Technical, result.Category);
        }

        [Fact]
        public void Calculate_ReasonContainsMACDValues()
        {
            var closes = Enumerable.Range(1, 50).Select(i => (decimal)(100 + i)).ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Contains("MACD=", result.Reason);
            Assert.Contains("Signal=", result.Reason);
        }
    }
}
