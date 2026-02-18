using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Volume ratio indicator calculator.
    /// Compares recent volume to historical average to detect volume anomalies.
    /// </summary>
    public class VolumeRatioCalculator : IIndicatorCalculator
    {
        public string Name => "VolumeRatio";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int SHORT_PERIOD = 5;
        private const int LONG_PERIOD = 20;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Prices.Count >= LONG_PERIOD;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var volumes = context.Volumes;

            var avgVolume5 = volumes[^SHORT_PERIOD..].Select(v => (decimal)v).Average();
            var avgVolume20 = volumes[^LONG_PERIOD..].Select(v => (decimal)v).Average();
            var todayVolume = (decimal)volumes[^1];

            var volumeRatio = avgVolume20 > 0 ? avgVolume5 / avgVolume20 : 1m;
            var todayVsAvg = avgVolume20 > 0 ? todayVolume / avgVolume20 : 1m;

            var (signal, direction, score) = DetermineVolumeSignal(volumeRatio, todayVsAvg);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = volumeRatio,
                SubValues = new Dictionary<string, decimal>
                {
                    ["VolumeRatio_5_20"] = volumeRatio,
                    ["TodayVsAvg20"] = todayVsAvg,
                    ["AvgVolume5"] = avgVolume5,
                    ["AvgVolume20"] = avgVolume20,
                    ["TodayVolume"] = todayVolume
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"5日均量/20日均量={volumeRatio:F2}，今日量/20日均量={todayVsAvg:F2}，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) DetermineVolumeSignal(
            decimal volumeRatio, decimal todayVsAvg)
        {
            // Volume expansion with price (need to combine with price direction for full picture)
            if (todayVsAvg > 2.0m)
                return ("爆量，成交量異常放大", SignalDirection.Neutral, 50); // Direction depends on price

            if (volumeRatio > 1.5m)
                return ("量能明顯放大", SignalDirection.Bullish, 65);

            if (volumeRatio > 1.2m)
                return ("量能溫和放大", SignalDirection.Bullish, 60);

            if (volumeRatio < 0.5m)
                return ("量能極度萎縮", SignalDirection.Bearish, 35);

            if (volumeRatio < 0.8m)
                return ("量能萎縮", SignalDirection.Neutral, 45);

            return ("量能正常", SignalDirection.Neutral, 50);
        }
    }
}
