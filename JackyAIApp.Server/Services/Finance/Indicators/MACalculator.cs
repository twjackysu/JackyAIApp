using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Moving Average indicator calculator.
    /// Calculates MA5, MA20, MA60 and determines trend alignment.
    /// </summary>
    public class MACalculator : IIndicatorCalculator
    {
        public string Name => "MA";
        public IndicatorCategory Category => IndicatorCategory.Technical;

        private const int MA5_PERIOD = 5;
        private const int MA20_PERIOD = 20;
        private const int MA60_PERIOD = 60;

        public bool CanCalculate(IndicatorContext context)
        {
            // Need at least MA20 worth of data for meaningful analysis
            return context.Prices.Count >= MA20_PERIOD;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var closes = context.ClosingPrices;
            var latestClose = closes[^1];

            var ma5 = CalculateMA(closes, MA5_PERIOD);
            var ma20 = CalculateMA(closes, MA20_PERIOD);
            var ma60 = closes.Length >= MA60_PERIOD ? CalculateMA(closes, MA60_PERIOD) : (decimal?)null;

            // Determine trend alignment
            var (signal, direction, score) = DetermineMASignal(latestClose, ma5, ma20, ma60);

            var result = new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = ma20,
                SubValues = new Dictionary<string, decimal>
                {
                    ["MA5"] = ma5,
                    ["MA20"] = ma20
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = BuildReason(latestClose, ma5, ma20, ma60)
            };

            if (ma60.HasValue)
            {
                result.SubValues["MA60"] = ma60.Value;
            }

            return result;
        }

        private static decimal CalculateMA(decimal[] prices, int period)
        {
            if (prices.Length < period)
                return prices[^1];

            return prices[^period..].Average();
        }

        private static (string signal, SignalDirection direction, int score) DetermineMASignal(
            decimal close, decimal ma5, decimal ma20, decimal? ma60)
        {
            bool aboveMa5 = close > ma5;
            bool aboveMa20 = close > ma20;
            bool ma5AboveMa20 = ma5 > ma20;

            if (ma60.HasValue)
            {
                bool aboveMa60 = close > ma60.Value;
                bool ma20AboveMa60 = ma20 > ma60.Value;

                // 多頭排列: Close > MA5 > MA20 > MA60
                if (aboveMa5 && ma5AboveMa20 && ma20AboveMa60)
                    return ("多頭排列", SignalDirection.StrongBullish, 90);

                // 空頭排列: Close < MA5 < MA20 < MA60
                if (!aboveMa5 && !ma5AboveMa20 && !ma20AboveMa60)
                    return ("空頭排列", SignalDirection.StrongBearish, 10);

                // 站上所有均線但未完全排列
                if (aboveMa5 && aboveMa20 && aboveMa60)
                    return ("偏多", SignalDirection.Bullish, 70);

                // 跌破所有均線
                if (!aboveMa5 && !aboveMa20 && !aboveMa60)
                    return ("偏空", SignalDirection.Bearish, 30);
            }
            else
            {
                // Only MA5 and MA20 available
                if (aboveMa5 && ma5AboveMa20)
                    return ("短期多頭", SignalDirection.Bullish, 75);

                if (!aboveMa5 && !ma5AboveMa20)
                    return ("短期空頭", SignalDirection.Bearish, 25);
            }

            // Mixed signals
            if (aboveMa20)
                return ("中期偏多，短期震盪", SignalDirection.Neutral, 55);

            return ("均線糾結", SignalDirection.Neutral, 50);
        }

        private static string BuildReason(decimal close, decimal ma5, decimal ma20, decimal? ma60)
        {
            var parts = new List<string>
            {
                $"收盤價 {close:F2}",
                $"MA5={ma5:F2}",
                $"MA20={ma20:F2}"
            };

            if (ma60.HasValue)
                parts.Add($"MA60={ma60.Value:F2}");

            return string.Join("，", parts);
        }
    }
}
