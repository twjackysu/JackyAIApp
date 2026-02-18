using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Foreign investor holding indicator calculator.
    /// Analyzes foreign investor ownership levels as a signal of institutional confidence.
    /// </summary>
    public class ForeignHoldingCalculator : IIndicatorCalculator
    {
        public string Name => "ForeignHolding";
        public IndicatorCategory Category => IndicatorCategory.Chip;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Chips?.ForeignHoldingPercentage != null
                && context.Chips.ForeignHoldingPercentage > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var chips = context.Chips!;
            var holdingPct = chips.ForeignHoldingPercentage!.Value;
            var upperLimit = chips.ForeignUpperLimit ?? 100m;

            // 外資持股接近上限的程度
            var nearLimit = upperLimit > 0
                ? Math.Round(holdingPct / upperLimit * 100, 2)
                : 0m;

            var (signal, direction, score) = DetermineForeignSignal(holdingPct, nearLimit);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = holdingPct,
                SubValues = new Dictionary<string, decimal>
                {
                    ["HoldingPercentage"] = holdingPct,
                    ["UpperLimit"] = upperLimit,
                    ["NearLimitRatio"] = nearLimit,
                    ["HoldingShares"] = chips.ForeignHoldingShares ?? 0
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"外資持股比率={holdingPct:F2}%，上限={upperLimit:F0}%，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) DetermineForeignSignal(
            decimal holdingPct, decimal nearLimitRatio)
        {
            // 外資持股極高 = 高度認可，但需注意賣壓風險
            if (holdingPct > 70)
                return ("外資持股極高，法人高度認可但注意賣壓", SignalDirection.Neutral, 55);

            if (holdingPct > 50)
                return ("外資持股過半，法人認可度高", SignalDirection.Bullish, 70);

            if (holdingPct > 30)
                return ("外資持股比重大，關注法人動向", SignalDirection.Bullish, 65);

            if (holdingPct > 15)
                return ("外資持股中等", SignalDirection.Neutral, 50);

            if (holdingPct > 5)
                return ("外資持股偏低", SignalDirection.Neutral, 45);

            return ("外資持股極低，法人關注度低", SignalDirection.Bearish, 35);
        }
    }
}
