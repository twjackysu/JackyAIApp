using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Dividend yield indicator — evaluates income return potential.
    /// </summary>
    public class DividendYieldCalculator : IIndicatorCalculator
    {
        public string Name => "DividendYield";
        public IndicatorCategory Category => IndicatorCategory.Fundamental;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Fundamentals?.DividendYield != null;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var dyOrDividend = context.Fundamentals!.DividendYield!.Value;

            // If value looks like annual dividend amount (not percentage),
            // compute yield = (annualDividend / price) * 100
            var dy = dyOrDividend;
            if (context.LatestClose.HasValue && context.LatestClose.Value > 0 && dyOrDividend > 0 && dyOrDividend < 50)
            {
                // If it's a small number and < price, it's likely annual dividend in dollars
                var computedYield = (dyOrDividend / context.LatestClose.Value) * 100;
                if (computedYield > 0 && computedYield < 30)
                    dy = computedYield;
            }
            var (signal, direction, score) = Evaluate(dy);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = dy,
                SubValues = new Dictionary<string, decimal> { ["DividendYield"] = dy },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"殖利率={dy:F2}%，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(decimal dy)
        {
            return dy switch
            {
                <= 0 => ("無配息", SignalDirection.Bearish, 30),
                < 2 => ("殖利率偏低", SignalDirection.Neutral, 45),
                < 4 => ("殖利率中等", SignalDirection.Neutral, 55),
                < 6 => ("殖利率不錯，具配息吸引力", SignalDirection.Bullish, 65),
                < 8 => ("高殖利率，配息豐厚", SignalDirection.Bullish, 72),
                _ => ("殖利率極高，留意是否能維持", SignalDirection.Bullish, 68)
            };
        }
    }
}
