using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// MACD (Moving Average Convergence Divergence) calculator.
    /// Uses standard parameters: EMA(12), EMA(26), Signal(9).
    /// </summary>
    public class MACDCalculator : IIndicatorCalculator
    {
        public string Name => "MACD";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int FAST_PERIOD = 12;
        private const int SLOW_PERIOD = 26;
        private const int SIGNAL_PERIOD = 9;

        public bool CanCalculate(IndicatorContext context)
        {
            // Need at least SLOW_PERIOD + SIGNAL_PERIOD data points
            return context.Prices.Count >= SLOW_PERIOD + SIGNAL_PERIOD;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var closes = context.ClosingPrices;
            var (macd, signal, histogram) = CalculateMACD(closes);

            // Check previous values for crossover detection
            var (prevMacd, prevSignal, _) = CalculateMACDAtIndex(closes, closes.Length - 2);
            var crossover = DetectCrossover(macd, signal, prevMacd, prevSignal);

            var (signalText, direction, score) = DetermineMACDSignal(macd, signal, histogram, crossover);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = macd,
                SubValues = new Dictionary<string, decimal>
                {
                    ["MACD"] = macd,
                    ["Signal"] = signal,
                    ["Histogram"] = histogram,
                    ["DIF"] = macd  // DIF is the same as MACD line in Taiwan convention
                },
                Signal = signalText,
                Direction = direction,
                Score = score,
                Reason = $"MACD={macd:F4}，Signal={signal:F4}，柱狀={histogram:F4}，{signalText}"
            };
        }

        internal static (decimal macd, decimal signal, decimal histogram) CalculateMACD(decimal[] closes)
        {
            return CalculateMACDAtIndex(closes, closes.Length - 1);
        }

        private static (decimal macd, decimal signal, decimal histogram) CalculateMACDAtIndex(decimal[] closes, int endIndex)
        {
            if (endIndex < SLOW_PERIOD + SIGNAL_PERIOD - 1)
                return (0, 0, 0);

            var data = closes[..(endIndex + 1)];

            var fastEMA = CalculateEMA(data, FAST_PERIOD);
            var slowEMA = CalculateEMA(data, SLOW_PERIOD);

            // DIF (MACD line) = Fast EMA - Slow EMA
            var difValues = new decimal[fastEMA.Length];
            var offset = slowEMA.Length - fastEMA.Length;

            // Align arrays: slowEMA starts later
            var minLen = Math.Min(fastEMA.Length, slowEMA.Length);
            var macdLine = new decimal[minLen];
            for (int i = 0; i < minLen; i++)
            {
                macdLine[i] = fastEMA[fastEMA.Length - minLen + i] - slowEMA[slowEMA.Length - minLen + i];
            }

            // Signal line = EMA of MACD line
            var signalLine = CalculateEMA(macdLine, SIGNAL_PERIOD);

            var macd = macdLine[^1];
            var signal = signalLine[^1];
            var histogram = macd - signal;

            return (macd, signal, histogram);
        }

        /// <summary>
        /// Calculate EMA series for given data and period.
        /// </summary>
        internal static decimal[] CalculateEMA(decimal[] data, int period)
        {
            if (data.Length < period)
                return data;

            var multiplier = 2m / (period + 1m);
            var ema = new decimal[data.Length - period + 1];

            // Seed with SMA
            decimal sum = 0;
            for (int i = 0; i < period; i++)
                sum += data[i];
            ema[0] = sum / period;

            // Calculate EMA
            for (int i = 1; i < ema.Length; i++)
            {
                ema[i] = (data[i + period - 1] - ema[i - 1]) * multiplier + ema[i - 1];
            }

            return ema;
        }

        private static string DetectCrossover(decimal macd, decimal signal, decimal prevMacd, decimal prevSignal)
        {
            if (prevMacd <= prevSignal && macd > signal)
                return "golden_cross";

            if (prevMacd >= prevSignal && macd < signal)
                return "death_cross";

            return "none";
        }

        private static (string signal, SignalDirection direction, int score) DetermineMACDSignal(
            decimal macd, decimal signal, decimal histogram, string crossover)
        {
            if (crossover == "golden_cross")
                return ("MACD金叉，買進訊號", SignalDirection.StrongBullish, 85);

            if (crossover == "death_cross")
                return ("MACD死叉，賣出訊號", SignalDirection.StrongBearish, 15);

            if (macd > 0 && histogram > 0)
                return ("MACD多方，動能增強", SignalDirection.Bullish, 70);

            if (macd > 0 && histogram < 0)
                return ("MACD多方，動能減弱", SignalDirection.Neutral, 55);

            if (macd < 0 && histogram < 0)
                return ("MACD空方，動能增強", SignalDirection.Bearish, 30);

            if (macd < 0 && histogram > 0)
                return ("MACD空方，動能減弱", SignalDirection.Neutral, 45);

            return ("MACD中性", SignalDirection.Neutral, 50);
        }
    }
}
