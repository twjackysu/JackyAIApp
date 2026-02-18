using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Director/Supervisor pledge ratio indicator calculator.
    /// High pledge ratios indicate potential forced selling risk.
    /// </summary>
    public class DirectorPledgeCalculator : IIndicatorCalculator
    {
        public string Name => "DirectorPledge";
        public IndicatorCategory Category => IndicatorCategory.Chip;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Chips?.DirectorHoldings != null
                && context.Chips.DirectorHoldings.Count > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var chips = context.Chips!;
            var pledgeRatio = chips.DirectorPledgeRatio;
            var totalShares = chips.TotalDirectorShares;
            var totalPledged = chips.TotalDirectorPledged;
            var directorCount = chips.DirectorHoldings!.Count;

            var (signal, direction, score) = DeterminePledgeSignal(pledgeRatio);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = pledgeRatio,
                SubValues = new Dictionary<string, decimal>
                {
                    ["PledgeRatio"] = pledgeRatio,
                    ["TotalDirectorShares"] = totalShares,
                    ["TotalPledgedShares"] = totalPledged,
                    ["DirectorCount"] = directorCount
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"董監事{directorCount}人，持股合計{totalShares:N0}股，" +
                         $"設質{totalPledged:N0}股({pledgeRatio:F2}%)，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) DeterminePledgeSignal(decimal pledgeRatio)
        {
            // 高設質比 = 經營者可能資金吃緊，股價下跌時有斷頭風險
            if (pledgeRatio > 50)
                return ("董監設質比極高，斷頭風險大", SignalDirection.StrongBearish, 10);

            if (pledgeRatio > 30)
                return ("董監設質比偏高，注意風險", SignalDirection.Bearish, 25);

            if (pledgeRatio > 15)
                return ("董監設質比中等，需留意", SignalDirection.Neutral, 45);

            if (pledgeRatio > 5)
                return ("董監設質比低，正常範圍", SignalDirection.Neutral, 55);

            if (pledgeRatio == 0)
                return ("董監零設質，經營者信心充足", SignalDirection.Bullish, 75);

            return ("董監設質比極低，體質健康", SignalDirection.Bullish, 70);
        }
    }
}
