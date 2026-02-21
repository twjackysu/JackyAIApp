using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// P/B ratio indicator — evaluates stock price relative to book value.
    /// </summary>
    public class PBRatioCalculator : IIndicatorCalculator
    {
        public string Name => "PBRatio";
        public IndicatorCategory Category => IndicatorCategory.Fundamental;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Fundamentals?.PBRatio != null && context.Fundamentals.PBRatio > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var pbOrBvps = context.Fundamentals!.PBRatio!.Value;

            // If value looks like BVPS (book value per share) rather than P/B ratio,
            // compute actual P/B using latest close price.
            // Heuristic: if BVPS is high and we have a price, compute P/B
            var pb = pbOrBvps;
            if (context.LatestClose.HasValue && context.LatestClose.Value > 0 && pbOrBvps > 0 && pbOrBvps < context.LatestClose.Value)
            {
                // Could be BVPS — check if computing P/B gives a reasonable result
                var computedPB = context.LatestClose.Value / pbOrBvps;
                if (computedPB > 0.1m && computedPB < 200m)
                    pb = computedPB;
            }
            var (signal, direction, score) = Evaluate(pb);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = pb,
                SubValues = new Dictionary<string, decimal> { ["PBRatio"] = pb },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"股價淨值比={pb:F2}，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(decimal pb)
        {
            return pb switch
            {
                < 0.5m => ("淨值比極低，可能有資產價值", SignalDirection.Bullish, 70),
                < 1.0m => ("股價低於淨值，相對便宜", SignalDirection.Bullish, 65),
                < 1.5m => ("淨值比合理", SignalDirection.Neutral, 55),
                < 3.0m => ("淨值比偏高", SignalDirection.Neutral, 45),
                < 5.0m => ("淨值比高，市場給予較高溢價", SignalDirection.Bearish, 38),
                _ => ("淨值比過高，估值偏貴", SignalDirection.Bearish, 30)
            };
        }
    }
}
