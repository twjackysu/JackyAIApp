using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Insider trading indicator — evaluates insider buying/selling activity (US stocks only).
    /// Based on SEC Form 4 filings (last 90 days).
    /// </summary>
    public class InsiderTradingCalculator : IIndicatorCalculator
    {
        public string Name => "InsiderTrading";
        public IndicatorCategory Category => IndicatorCategory.Chip;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.InsiderTrading != null 
                && context.InsiderTrading.RecentTransactions.Count > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var insider = context.InsiderTrading!;
            var (signal, direction, score) = Evaluate(insider);

            var subValues = new Dictionary<string, decimal>
            {
                ["PurchaseCount"] = insider.PurchaseCount,
                ["SaleCount"] = insider.SaleCount,
                ["NetBuyingShares"] = insider.NetBuyingShares
            };
            if (insider.NetBuyingValue.HasValue)
                subValues["NetBuyingValue"] = insider.NetBuyingValue.Value;

            var reason = BuildReason(insider, signal);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = insider.NetBuyingShares, // Use net buying shares as primary value
                SubValues = subValues,
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = reason
            };
        }

        private static (string signal, SignalDirection direction, int score) Evaluate(InsiderTradingSummary insider)
        {
            var netBuyingShares = insider.NetBuyingShares;
            var totalActivity = insider.PurchaseCount + insider.SaleCount;

            // Low activity → neutral
            if (totalActivity < 3 && Math.Abs(netBuyingShares) < 5_000)
                return ("內部人交易活動低，訊號不明", SignalDirection.Neutral, 50);

            // Strong buying signals (share-based, no price needed)
            if (netBuyingShares > 100_000 && insider.PurchaseCount > insider.SaleCount * 2)
                return ("內部人大量買進，高度看好公司前景", SignalDirection.StrongBullish, 80);

            if (netBuyingShares > 50_000 && insider.PurchaseCount > insider.SaleCount)
                return ("內部人持續買進，看好公司", SignalDirection.Bullish, 70);

            if (netBuyingShares > 10_000)
                return ("內部人淨買進，偏多訊號", SignalDirection.Bullish, 62);

            if (netBuyingShares > 0)
                return ("內部人小幅淨買進", SignalDirection.Neutral, 55);

            // Selling signals
            if (netBuyingShares < -100_000 && insider.SaleCount > insider.PurchaseCount * 2)
                return ("內部人大量賣出，高度看淡", SignalDirection.StrongBearish, 25);

            if (netBuyingShares < -50_000 && insider.SaleCount > insider.PurchaseCount)
                return ("內部人持續賣出，看淡公司", SignalDirection.Bearish, 35);

            if (netBuyingShares < -10_000)
                return ("內部人淨賣出，偏空訊號", SignalDirection.Bearish, 40);

            // Default: slight selling or neutral
            return ("內部人小幅淨賣出", SignalDirection.Neutral, 45);
        }

        private static string BuildReason(InsiderTradingSummary insider, string signal)
        {
            var parts = new List<string>();

            if (insider.NetBuyingValue.HasValue)
            {
                var netValueM = insider.NetBuyingValue.Value / 1_000_000m;
                parts.Add($"淨買賣金額={netValueM:+#,##0.0;-#,##0.0;0}M USD");
            }

            parts.Add($"買進{insider.PurchaseCount}筆");
            parts.Add($"賣出{insider.SaleCount}筆");
            parts.Add($"淨買賣股數={insider.NetBuyingShares:+#,##0;-#,##0;0}");
            parts.Add($"{signal}");

            return string.Join("，", parts);
        }
    }
}
