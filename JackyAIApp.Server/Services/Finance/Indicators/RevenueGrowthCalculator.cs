using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Revenue growth indicator — evaluates monthly revenue trends (YoY and MoM).
    /// </summary>
    public class RevenueGrowthCalculator : IIndicatorCalculator
    {
        public string Name => "RevenueGrowth";
        public IndicatorCategory Category => IndicatorCategory.Fundamental;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Fundamentals?.MonthlyRevenue != null;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var fund = context.Fundamentals!;
            var yoy = fund.RevenueYoY ?? 0;
            var mom = fund.RevenueMoM ?? 0;
            var revenue = fund.MonthlyRevenue!.Value;

            var (signal, direction, score) = Evaluate(yoy, mom);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = yoy,
                SubValues = new Dictionary<string, decimal>
                {
                    ["MonthlyRevenue"] = revenue,
                    ["RevenueYoY"] = yoy,
                    ["RevenueMoM"] = mom
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"月營收={revenue / 1000:F0}百萬，年增={yoy:F1}%，月增={mom:F1}%，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(decimal yoy, decimal mom)
        {
            // Combined evaluation of YoY and MoM
            if (yoy > 20 && mom > 0)
                return ("營收年增強勁且月增正成長", SignalDirection.StrongBullish, 80);

            if (yoy > 10)
                return ("營收年增雙位數成長", SignalDirection.Bullish, 70);

            if (yoy > 0 && mom > 0)
                return ("營收年增且月增正成長", SignalDirection.Bullish, 62);

            if (yoy > 0)
                return ("營收年增正成長", SignalDirection.Neutral, 55);

            if (yoy > -10)
                return ("營收小幅衰退", SignalDirection.Neutral, 45);

            if (yoy > -20)
                return ("營收明顯衰退", SignalDirection.Bearish, 35);

            return ("營收大幅衰退", SignalDirection.StrongBearish, 25);
        }
    }
}
