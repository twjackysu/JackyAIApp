using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// EPS indicator — evaluates profitability based on earnings per share.
    /// </summary>
    public class EPSCalculator : IIndicatorCalculator
    {
        public string Name => "EPS";
        public IndicatorCategory Category => IndicatorCategory.Fundamental;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Fundamentals?.EPS != null;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var fund = context.Fundamentals!;
            var eps = fund.EPS!.Value;
            var (signal, direction, score) = Evaluate(eps);

            var subValues = new Dictionary<string, decimal> { ["EPS"] = eps };
            if (fund.OperatingIncome.HasValue)
                subValues["OperatingIncome"] = fund.OperatingIncome.Value;
            if (fund.NetIncome.HasValue)
                subValues["NetIncome"] = fund.NetIncome.Value;

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = eps,
                SubValues = subValues,
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"每股盈餘={eps:F2}元，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(decimal eps)
        {
            return eps switch
            {
                < 0 => ("EPS 為負，公司處於虧損狀態", SignalDirection.StrongBearish, 20),
                < 0.5m => ("EPS 偏低，獲利能力不足", SignalDirection.Bearish, 35),
                < 1.0m => ("EPS 尚可", SignalDirection.Neutral, 45),
                < 2.0m => ("EPS 中等", SignalDirection.Neutral, 55),
                < 5.0m => ("EPS 不錯，獲利穩健", SignalDirection.Bullish, 65),
                < 10.0m => ("EPS 優秀，獲利能力強", SignalDirection.Bullish, 72),
                _ => ("EPS 非常優秀", SignalDirection.StrongBullish, 78)
            };
        }
    }
}
