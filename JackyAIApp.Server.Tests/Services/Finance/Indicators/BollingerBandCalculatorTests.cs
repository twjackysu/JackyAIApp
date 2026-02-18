using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class BollingerBandCalculatorTests
    {
        private readonly BollingerBandCalculator _calculator = new();

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
            var closes = Enumerable.Range(1, 20).Select(i => (decimal)(100 + i)).ToArray();
            Assert.True(_calculator.CanCalculate(CreateContext(closes)));
        }

        [Fact]
        public void Calculate_UpperBandAboveMiddle()
        {
            var closes = Enumerable.Range(1, 25)
                .Select(i => 100m + (decimal)Math.Sin(i) * 5)
                .ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.True(result.SubValues["UpperBand"] > result.SubValues["MiddleBand"]);
        }

        [Fact]
        public void Calculate_LowerBandBelowMiddle()
        {
            var closes = Enumerable.Range(1, 25)
                .Select(i => 100m + (decimal)Math.Sin(i) * 5)
                .ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.True(result.SubValues["LowerBand"] < result.SubValues["MiddleBand"]);
        }

        [Fact]
        public void Calculate_MiddleBandEqualsMA20()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)i).ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            // MA20 of last 20 values (6..25) = 15.5
            var expectedMA20 = closes[^20..].Average();
            Assert.Equal(expectedMA20, result.SubValues["MiddleBand"]);
        }

        [Fact]
        public void Calculate_NarrowBandwidth_WhenConstantPrice()
        {
            // All same price → stddev ≈ 0 → bandwidth ≈ 0
            var closes = Enumerable.Range(1, 25).Select(i => 100m).ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Equal(0m, result.SubValues["Bandwidth"]);
        }

        [Fact]
        public void Calculate_PercentB_At50_WhenAtMiddle()
        {
            // When close = middle band, %B should be around 50
            var closes = Enumerable.Range(1, 25).Select(i => 100m).ToArray();
            // %B is undefined when bands are equal, should return 50
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Equal(50m, result.SubValues["%B"]);
        }

        [Fact]
        public void Calculate_DetectsBreakAboveUpper()
        {
            // Sudden spike → close above upper band
            var closes = new decimal[25];
            for (int i = 0; i < 24; i++) closes[i] = 100;
            closes[24] = 150; // big spike

            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Contains("上軌", result.Signal);
        }

        [Fact]
        public void Calculate_DetectsBreakBelowLower()
        {
            // Sudden drop → close below lower band
            var closes = new decimal[25];
            for (int i = 0; i < 24; i++) closes[i] = 100;
            closes[24] = 50; // big drop

            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Contains("下軌", result.Signal);
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var closes = Enumerable.Range(1, 25).Select(i => (decimal)(100 + i)).ToArray();
            var result = _calculator.Calculate(CreateContext(closes));

            Assert.Equal("BollingerBands", result.Name);
            Assert.Equal(IndicatorCategory.Technical, result.Category);
        }
    }
}
