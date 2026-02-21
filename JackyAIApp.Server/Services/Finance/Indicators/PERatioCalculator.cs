using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// P/E ratio indicator — evaluates stock valuation relative to earnings.
    /// </summary>
    public class PERatioCalculator : IIndicatorCalculator
    {
        public string Name => "PERatio";
        public IndicatorCategory Category => IndicatorCategory.Fundamental;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Fundamentals?.PERatio != null && context.Fundamentals.PERatio > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var fund = context.Fundamentals!;
            var pe = fund.PERatio!.Value;
            var (signal, direction, score) = Evaluate(pe);

            var subValues = new Dictionary<string, decimal> { ["PERatio"] = pe };
            if (fund.TrailingEPS.HasValue) subValues["TrailingEPS"] = fund.TrailingEPS.Value;

            var reason = $"本益比={pe:F2}";
            if (fund.TrailingEPS.HasValue)
                reason += $"（EPS={fund.TrailingEPS.Value:F2}";
            if (!string.IsNullOrEmpty(fund.EpsDataPeriod))
                reason += fund.TrailingEPS.HasValue ? $", {fund.EpsDataPeriod}" : $"（{fund.EpsDataPeriod}";
            if (fund.TrailingEPS.HasValue || !string.IsNullOrEmpty(fund.EpsDataPeriod))
                reason += "）";
            reason += $"，{signal}";

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = pe,
                SubValues = subValues,
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = reason
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(decimal pe)
        {
            return pe switch
            {
                < 0 => ("本益比為負（虧損中）", SignalDirection.StrongBearish, 20),
                < 10 => ("本益比偏低，股價相對便宜", SignalDirection.Bullish, 70),
                < 15 => ("本益比合理偏低", SignalDirection.Bullish, 65),
                < 20 => ("本益比合理", SignalDirection.Neutral, 55),
                < 30 => ("本益比偏高，留意估值風險", SignalDirection.Bearish, 40),
                < 50 => ("本益比偏高", SignalDirection.Bearish, 35),
                _ => ("本益比過高，估值風險大", SignalDirection.StrongBearish, 25)
            };
        }
    }
}
