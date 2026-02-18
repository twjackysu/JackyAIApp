using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// KD Stochastic Oscillator calculator.
    /// Uses standard parameters: K period=9, K smooth=3, D smooth=3.
    /// </summary>
    public class KDCalculator : IIndicatorCalculator
    {
        public string Name => "KD";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int RSV_PERIOD = 9;
        private const int K_SMOOTH = 3;
        private const int D_SMOOTH = 3;
        private const decimal OVERBOUGHT = 80m;
        private const decimal OVERSOLD = 20m;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Prices.Count >= RSV_PERIOD + K_SMOOTH + D_SMOOTH;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var prices = context.Prices;
            var (k, d) = CalculateKD(prices);

            // Previous values for crossover
            var (prevK, prevD) = CalculateKDAtIndex(prices, prices.Count - 2);
            var crossover = DetectCrossover(k, d, prevK, prevD);

            var (signal, direction, score) = DetermineKDSignal(k, d, crossover);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = k,
                SubValues = new Dictionary<string, decimal>
                {
                    ["K"] = k,
                    ["D"] = d,
                    ["RSV"] = CalculateRSV(prices, prices.Count - 1)
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"K={k:F2}，D={d:F2}，{signal}"
            };
        }

        internal static (decimal k, decimal d) CalculateKD(List<DailyPrice> prices)
        {
            return CalculateKDAtIndex(prices, prices.Count - 1);
        }

        private static (decimal k, decimal d) CalculateKDAtIndex(List<DailyPrice> prices, int endIndex)
        {
            if (endIndex < RSV_PERIOD - 1)
                return (50, 50);

            // Calculate RSV series
            var rsvSeries = new List<decimal>();
            for (int i = RSV_PERIOD - 1; i <= endIndex; i++)
            {
                rsvSeries.Add(CalculateRSV(prices, i));
            }

            // K = 2/3 * previous K + 1/3 * RSV (Taiwan convention)
            decimal k = 50m; // Initial K value
            foreach (var rsv in rsvSeries)
            {
                k = (2m / 3m) * k + (1m / 3m) * rsv;
            }

            // D = 2/3 * previous D + 1/3 * K
            // We need to recalculate properly
            decimal d = 50m; // Initial D value
            decimal tempK = 50m;
            foreach (var rsv in rsvSeries)
            {
                tempK = (2m / 3m) * tempK + (1m / 3m) * rsv;
                d = (2m / 3m) * d + (1m / 3m) * tempK;
            }

            return (k, d);
        }

        private static decimal CalculateRSV(List<DailyPrice> prices, int index)
        {
            if (index < RSV_PERIOD - 1)
                return 50m;

            var startIndex = index - RSV_PERIOD + 1;
            var periodPrices = prices.GetRange(startIndex, RSV_PERIOD);

            var highestHigh = periodPrices.Max(p => p.High);
            var lowestLow = periodPrices.Min(p => p.Low);
            var close = prices[index].Close;

            if (highestHigh == lowestLow)
                return 50m;

            return (close - lowestLow) / (highestHigh - lowestLow) * 100m;
        }

        private static string DetectCrossover(decimal k, decimal d, decimal prevK, decimal prevD)
        {
            // K crosses above D in oversold zone = strong buy
            if (prevK <= prevD && k > d)
                return k < 50 ? "golden_cross_low" : "golden_cross_high";

            // K crosses below D in overbought zone = strong sell
            if (prevK >= prevD && k < d)
                return k > 50 ? "death_cross_high" : "death_cross_low";

            return "none";
        }

        private static (string signal, SignalDirection direction, int score) DetermineKDSignal(
            decimal k, decimal d, string crossover)
        {
            switch (crossover)
            {
                case "golden_cross_low":
                    return ("KD低檔金叉，強烈買進訊號", SignalDirection.StrongBullish, 90);
                case "golden_cross_high":
                    return ("KD高檔金叉，偏多", SignalDirection.Bullish, 65);
                case "death_cross_high":
                    return ("KD高檔死叉，強烈賣出訊號", SignalDirection.StrongBearish, 10);
                case "death_cross_low":
                    return ("KD低檔死叉，偏空", SignalDirection.Bearish, 35);
            }

            if (k > OVERBOUGHT && d > OVERBOUGHT)
                return ("KD超買區，注意回檔", SignalDirection.Bearish, 25);

            if (k < OVERSOLD && d < OVERSOLD)
                return ("KD超賣區，留意反彈", SignalDirection.Bullish, 75);

            if (k > d)
                return ("K值在D值之上，偏多", SignalDirection.Bullish, 60);

            return ("K值在D值之下，偏空", SignalDirection.Bearish, 40);
        }
    }
}
