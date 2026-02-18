using JackyAIApp.Server.DTO.Finance;
using JackyAIApp.Server.Services.Finance.Indicators;

namespace JackyAIApp.Server.Tests.Services.Finance.Indicators
{
    public class VolumeRatioCalculatorTests
    {
        private readonly VolumeRatioCalculator _calculator = new();

        private static IndicatorContext CreateContext(long[] volumes)
        {
            var prices = volumes.Select((v, i) => new DailyPrice
            {
                Date = DateTime.Today.AddDays(-volumes.Length + i + 1),
                Open = 100,
                High = 101,
                Low = 99,
                Close = 100,
                Volume = v
            }).ToList();

            return new IndicatorContext { StockCode = "2330", Prices = prices };
        }

        [Fact]
        public void CanCalculate_ReturnsFalse_WhenInsufficientData()
        {
            var context = CreateContext(new long[] { 1000, 2000, 3000 });
            Assert.False(_calculator.CanCalculate(context));
        }

        [Fact]
        public void CanCalculate_ReturnsTrue_WhenEnoughData()
        {
            var volumes = Enumerable.Range(1, 20).Select(i => (long)1000).ToArray();
            Assert.True(_calculator.CanCalculate(CreateContext(volumes)));
        }

        [Fact]
        public void Calculate_RatioIsOne_WhenVolumeConstant()
        {
            var volumes = Enumerable.Range(1, 25).Select(i => (long)5000).ToArray();
            var result = _calculator.Calculate(CreateContext(volumes));

            Assert.Equal(1m, result.Value);
            Assert.Contains("正常", result.Signal);
        }

        [Fact]
        public void Calculate_DetectsExpansion()
        {
            // First 15 days: low volume, last 10 days gradually increasing, last 5 very high
            var volumes = new long[25];
            for (int i = 0; i < 15; i++) volumes[i] = 1000;
            for (int i = 15; i < 20; i++) volumes[i] = 2000;
            for (int i = 20; i < 25; i++) volumes[i] = 8000;

            var result = _calculator.Calculate(CreateContext(volumes));

            // 5-day avg = 8000, 20-day avg = (5*2000 + 5*8000 + 10*1000) / 20 = 3000
            // ratio = 8000/3000 ≈ 2.67
            Assert.True(result.Value > 1.2m, $"Expected volume ratio > 1.2, got {result.Value}");
        }

        [Fact]
        public void Calculate_DetectsContraction()
        {
            // 20-day: normal volume, last 5 days: very low
            var volumes = new long[25];
            for (int i = 0; i < 20; i++) volumes[i] = 10000;
            for (int i = 20; i < 25; i++) volumes[i] = 1000;

            var result = _calculator.Calculate(CreateContext(volumes));

            Assert.True(result.Value < 0.5m);
            Assert.Contains("萎縮", result.Signal);
        }

        [Fact]
        public void Calculate_DetectsExtremeVolume()
        {
            // Last day volume is 3x the 20-day average
            var volumes = new long[25];
            for (int i = 0; i < 24; i++) volumes[i] = 5000;
            volumes[24] = 30000; // 6x average

            var result = _calculator.Calculate(CreateContext(volumes));

            Assert.True(result.SubValues["TodayVsAvg20"] > 2);
            Assert.Contains("爆量", result.Signal);
        }

        [Fact]
        public void Calculate_NameAndCategory()
        {
            var volumes = Enumerable.Range(1, 25).Select(i => (long)5000).ToArray();
            var result = _calculator.Calculate(CreateContext(volumes));

            Assert.Equal("VolumeRatio", result.Name);
            Assert.Equal(IndicatorCategory.Technical, result.Category);
        }
    }
}
