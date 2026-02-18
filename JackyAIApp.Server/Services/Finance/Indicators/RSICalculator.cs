using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Relative Strength Index (RSI) calculator.
    /// Uses the standard 14-period RSI with Wilder's smoothing method.
    /// </summary>
    public class RSICalculator : IIndicatorCalculator
    {
        public string Name => "RSI";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int DEFAULT_PERIOD = 14;
        private const decimal OVERBOUGHT_THRESHOLD = 70m;
        private const decimal OVERSOLD_THRESHOLD = 30m;
        private const decimal EXTREME_OVERBOUGHT = 80m;
        private const decimal EXTREME_OVERSOLD = 20m;

        public bool CanCalculate(IndicatorContext context)
        {
            // Need at least period + 1 prices to calculate RSI
            return context.Prices.Count > DEFAULT_PERIOD;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var closes = context.ClosingPrices;
            var rsi = CalculateRSI(closes, DEFAULT_PERIOD);

            var (signal, direction, score) = DetermineRSISignal(rsi);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = rsi,
                SubValues = new Dictionary<string, decimal>
                {
                    ["RSI14"] = rsi
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"RSI(14)={rsi:F2}，{signal}"
            };
        }

        /// <summary>
        /// Calculate RSI using Wilder's smoothing method (exponential moving average).
        /// </summary>
        internal static decimal CalculateRSI(decimal[] closes, int period)
        {
            if (closes.Length <= period)
                return 50m; // Default neutral

            // Calculate price changes
            var changes = new decimal[closes.Length - 1];
            for (int i = 0; i < changes.Length; i++)
            {
                changes[i] = closes[i + 1] - closes[i];
            }

            // Initial average gain/loss (simple average for first period)
            decimal avgGain = 0, avgLoss = 0;
            for (int i = 0; i < period; i++)
            {
                if (changes[i] > 0) avgGain += changes[i];
                else avgLoss += Math.Abs(changes[i]);
            }
            avgGain /= period;
            avgLoss /= period;

            // Wilder's smoothing for remaining periods
            for (int i = period; i < changes.Length; i++)
            {
                var gain = changes[i] > 0 ? changes[i] : 0;
                var loss = changes[i] < 0 ? Math.Abs(changes[i]) : 0;

                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
            }

            if (avgLoss == 0)
                return 100m;

            var rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
        }

        private static (string signal, SignalDirection direction, int score) DetermineRSISignal(decimal rsi)
        {
            if (rsi >= EXTREME_OVERBOUGHT)
                return ("極度超買，注意回檔風險", SignalDirection.StrongBearish, 15);

            if (rsi >= OVERBOUGHT_THRESHOLD)
                return ("超買區間，宜謹慎", SignalDirection.Bearish, 30);

            if (rsi <= EXTREME_OVERSOLD)
                return ("極度超賣，可能反彈", SignalDirection.StrongBullish, 85);

            if (rsi <= OVERSOLD_THRESHOLD)
                return ("超賣區間，留意買點", SignalDirection.Bullish, 70);

            if (rsi >= 50)
                return ("中性偏多", SignalDirection.Bullish, 60);

            return ("中性偏空", SignalDirection.Bearish, 40);
        }
    }
}
