using JackyAIApp.Server.DTO.Finance;

namespace JackyAIApp.Server.Services.Finance.Indicators
{
    /// <summary>
    /// Margin trading indicator calculator.
    /// Analyzes 融資融券 data to determine retail sentiment and leverage risk.
    /// </summary>
    public class MarginIndicatorCalculator : IIndicatorCalculator
    {
        public string Name => "MarginTrading";
        public IndicatorCategory Category => IndicatorCategory.Chip;

        public bool CanCalculate(IndicatorContext context)
        {
            return context.Chips?.MarginBalance != null && context.Chips.MarginBalance > 0;
        }

        public IndicatorResult Calculate(IndicatorContext context)
        {
            var chips = context.Chips!;

            var marginBalance = chips.MarginBalance!.Value;
            var marginLimit = chips.MarginLimit ?? 0;
            var shortBalance = chips.ShortBalance ?? 0;
            var marginPrev = chips.MarginPreviousBalance ?? marginBalance;
            var shortPrev = chips.ShortPreviousBalance ?? shortBalance;
            var offsetVolume = chips.OffsetVolume ?? 0;

            // 融資使用率 = 融資餘額 / 融資限額
            var marginUtilization = marginLimit > 0
                ? Math.Round((decimal)marginBalance / marginLimit * 100, 2)
                : 0m;

            // 融資增減
            var marginChange = marginBalance - marginPrev;

            // 融券增減
            var shortChange = shortBalance - shortPrev;

            // 券資比 = 融券餘額 / 融資餘額
            var shortMarginRatio = marginBalance > 0
                ? Math.Round((decimal)shortBalance / marginBalance * 100, 2)
                : 0m;

            var (signal, direction, score) = DetermineMarginSignal(
                marginUtilization, marginChange, shortChange, shortMarginRatio, offsetVolume);

            return new IndicatorResult
            {
                Name = Name,
                Category = Category,
                Value = marginUtilization,
                SubValues = new Dictionary<string, decimal>
                {
                    ["MarginBalance"] = marginBalance,
                    ["MarginChange"] = marginChange,
                    ["MarginUtilization"] = marginUtilization,
                    ["ShortBalance"] = shortBalance,
                    ["ShortChange"] = shortChange,
                    ["ShortMarginRatio"] = shortMarginRatio,
                    ["OffsetVolume"] = offsetVolume
                },
                Signal = signal,
                Direction = direction,
                Score = score,
                Reason = $"融資餘額={marginBalance:N0}張(日增{marginChange:+#;-#;0})，融券={shortBalance:N0}張，" +
                         $"券資比={shortMarginRatio:F2}%，融資使用率={marginUtilization:F2}%，{signal}"
            };
        }

        private static (string signal, SignalDirection direction, int score) DetermineMarginSignal(
            decimal marginUtilization, long marginChange, long shortChange, decimal shortMarginRatio, long offsetVolume)
        {
            // 高券資比 = 潛在軋空動能
            if (shortMarginRatio > 30)
                return ("高券資比，軋空動能強", SignalDirection.Bullish, 75);

            if (shortMarginRatio > 20)
                return ("券資比偏高，留意軋空", SignalDirection.Bullish, 65);

            // 融資使用率過高 = 散戶過度槓桿，風險大
            if (marginUtilization > 80)
                return ("融資使用率極高，散戶過度槓桿", SignalDirection.StrongBearish, 15);

            if (marginUtilization > 60)
                return ("融資使用率偏高，注意風險", SignalDirection.Bearish, 30);

            // 融資大增 = 散戶追高，偏空訊號
            if (marginChange > 1000)
                return ("融資大幅增加，散戶追高", SignalDirection.Bearish, 35);

            // 融資大減 = 散戶認賠或主力洗盤，可能是底部
            if (marginChange < -1000)
                return ("融資大幅減少，籌碼沉澱", SignalDirection.Bullish, 70);

            // 融券大增 = 看空力道增加
            if (shortChange > 500)
                return ("融券增加，放空力道增強", SignalDirection.Bearish, 35);

            // 資券互抵大 = 當沖熱絡
            if (offsetVolume > 500)
                return ("資券互抵量大，當沖活躍", SignalDirection.Neutral, 50);

            // 低融資使用率 = 健康
            if (marginUtilization < 20)
                return ("融資使用率低，籌碼健康", SignalDirection.Bullish, 65);

            return ("融資融券正常", SignalDirection.Neutral, 50);
        }
    }
}
