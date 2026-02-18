using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Bollinger Bands calculator.
    /// Uses MA20 ± 2 standard deviations.
    /// </summary>
    public class BollingerBandCalculator : IIndicatorCalculator
    {
        public string Name => "BollingerBands";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int PERIOD = 20;
        private const decimal MULTIPLIER = 2m;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Prices.Count >= PERIOD;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var closes = context.ClosingPrices;
            var latestClose = closes[^1];

            var periodCloses = closes[^PERIOD..];
            var ma = periodCloses.Average();
            var stdDev = CalculateStdDev(periodCloses, ma);

            var upperBand = ma + MULTIPLIER * stdDev;
            var lowerBand = ma - MULTIPLIER * stdDev;
            var bandwidth = ma > 0 ? (upperBand - lowerBand) / ma * 100m : 0;

            // %B = (Close - Lower) / (Upper - Lower)
            var percentB = (upperBand - lowerBand) > 0
                ? (latestClose - lowerBand) / (upperBand - lowerBand) * 100m
                : 50m;

            var (signal, direction, score) = DetermineBBSignal(latestClose, upperBand, lowerBand, percentB, bandwidth);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = percentB,
                SubValues = new Dictionary<string, decimal>
                {
                    ["UpperBand"] = upperBand,
                    ["MiddleBand"] = ma,
                    ["LowerBand"] = lowerBand,
                    ["Bandwidth"] = bandwidth,
                    ["%B"] = percentB
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"上軌={upperBand:F2}，中軌={ma:F2}，下軌={lowerBand:F2}，%B={percentB:F1}%，{signal}"
            };
        }

        private static decimal CalculateStdDev(decimal[] values, decimal mean)
        {
            var sumOfSquares = values.Sum(v => (v - mean) * (v - mean));
            return (decimal)Math.Sqrt((double)(sumOfSquares / values.Length));
        }

        private static (string signal, SignalDirection direction, int score) DetermineBBSignal(
            decimal close, decimal upper, decimal lower, decimal percentB, decimal bandwidth)
        {
            // Price above upper band
            if (close >= upper)
                return ("突破布林上軌，可能過熱", SignalDirection.Bearish, 25);

            // Price below lower band
            if (close <= lower)
                return ("跌破布林下軌，可能超賣", SignalDirection.Bullish, 75);

            // Near upper band (within 5%)
            if (percentB > 90)
                return ("接近布林上軌，注意壓力", SignalDirection.Bearish, 35);

            // Near lower band (within 5%)
            if (percentB < 10)
                return ("接近布林下軌，留意支撐", SignalDirection.Bullish, 65);

            // Bandwidth squeeze (potential breakout)
            if (bandwidth < 5m)
                return ("布林通道收窄，可能即將突破", SignalDirection.Neutral, 50);

            // Above middle band
            if (percentB > 50)
                return ("價格在布林中軌之上，偏多", SignalDirection.Bullish, 60);

            return ("價格在布林中軌之下，偏空", SignalDirection.Bearish, 40);
        }
    }
}
