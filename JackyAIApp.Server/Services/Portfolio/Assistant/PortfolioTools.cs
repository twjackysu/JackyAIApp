using System.Globalization;
using System.Text;
using System.Text.Json;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Portfolio.Assistant
{
    /// <summary>
    /// The reader's portfolio as /portfolio values it (<see cref="IPortfolioService.GetPortfolioSummaryAsync"/>),
    /// for Studio AI: the headline figures as text for the model, and the positions as a table card. A read of
    /// the reader's own data that they asked about; on the connectors page they may make it ask first, or turn it off.
    /// </summary>
    public sealed class PortfolioSummaryTool(IPortfolioService portfolio) : ISiteTool
    {
        public const string Name = "portfolio_summary";

        /// <summary>The most positions the model is told about (by value); the card lists them all.</summary>
        internal const int MaxPositionsInText = 15;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "投資組合摘要",
            "讀取使用者在本站記錄的投資組合（最新快照，用目前價格估值）：淨資產、各類資產金額、未實現損益、含息報酬、" +
            "今日變化、市場曝險，以及各持倉的市值與損益。使用者問到自己的資產、持股、損益或配置時使用。",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            PortfolioSummaryDto summary;
            try
            {
                summary = await portfolio.GetPortfolioSummaryAsync(context.UserId, ct: ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return AiToolResult.Error("投資組合暫時讀不到，請告訴使用者稍後再試，或直接打開投資組合頁面。");
            }

            if (summary.Positions.Count == 0)
                return new AiToolResult("使用者還沒有在本站記錄任何持倉。可以請他到投資組合頁面輸入。", Card: new
                {
                    kind = "link",
                    title = "投資組合",
                    href = "/portfolio",
                    description = "記錄股票、基金、現金、負債與不動產，看淨資產與報酬",
                    actionLabel = "開始記錄",
                });

            return new AiToolResult(Describe(summary), Card: CardOf(summary));
        }

        /// <summary>The figures for the model, in TWD.</summary>
        internal static string Describe(PortfolioSummaryDto s)
        {
            var text = new StringBuilder();
            text.AppendLine($"投資組合（快照日期 {s.EffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "未知"}，金額單位 TWD，USD/TWD {s.UsdTwdRate.ToString("0.###", CultureInfo.InvariantCulture)}）");
            text.AppendLine($"淨資產 {Money(s.TotalNetWorthTWD)}：台股 {Money(s.TwStockTWD)}、美股 {Money(s.UsStockTWD)}、基金 {Money(s.FundTWD)}、現金 {Money(s.CashTWD)}、不動產 {Money(s.RealEstateTWD)}、負債 -{Money(s.LiabilityTWD)}");
            text.AppendLine($"未實現損益 {Signed(s.UnrealizedPnlTWD)}（{Pct(s.UnrealizedPnlPct)}，已扣估計賣出成本 {Money(s.EstimatedSellCostTWD)}）；累計股利 {Money(s.TotalDividendsTWD)}；含息報酬 {Signed(s.TotalReturnPnlTWD)}（{Pct(s.TotalReturnPct)}）");
            if (s.AnnualizedReturnPct is { } xirr) text.AppendLine($"年化報酬（XIRR）{Pct(xirr)}");
            if (s.DayChangeTWD is { } day) text.AppendLine($"今日變化 {Signed(day)}{(s.DayChangePct is { } p ? $"（{Pct(p)}）" : "")}");
            text.Append($"市場曝險 {Money(s.MarketExposureTWD)}");
            if (s.ExposureOfLiquidAssetsPct is { } water) text.Append($"，水位 {Pct(water)}");
            if (s.LiquidExposureMultiple is { } multiple) text.Append($"，槓桿 {multiple.ToString("0.00", CultureInfo.InvariantCulture)}×");
            text.AppendLine();

            var positions = s.Positions.OrderByDescending(p => Math.Abs(p.MarketValueTWD)).ToList();
            text.AppendLine($"持倉 {positions.Count} 筆（依市值）：");
            foreach (var p in positions.Take(MaxPositionsInText))
            {
                text.Append($"- {p.Name}（{p.Symbol}，{p.AssetType}）市值 {Money(p.MarketValueTWD)}");
                if (p.UnrealizedPnlTWD is { } pnl) text.Append($"，損益 {Signed(pnl)}{(p.UnrealizedPnlPct is { } pp ? $"（{Pct(pp)}）" : "")}");
                if (p.ExposureFactor != 1m) text.Append($"，曝險係數 {p.ExposureFactor.ToString("0.##", CultureInfo.InvariantCulture)}");
                if (p.PriceFetchFailed) text.Append("（今天的價格沒取到，用最後已知價格）");
                text.AppendLine();
            }
            if (positions.Count > MaxPositionsInText) text.AppendLine($"…另有 {positions.Count - MaxPositionsInText} 筆較小的持倉。");
            return text.ToString().TrimEnd();
        }

        /// <summary>The positions as a table card, largest first.</summary>
        internal static object CardOf(PortfolioSummaryDto s) => new
        {
            kind = "table",
            title = $"投資組合　淨資產 {Money(s.TotalNetWorthTWD)} TWD",
            columns = new object[]
            {
                new { key = "name", label = "持倉" },
                new { key = "value", label = "市值", numeric = true },
                new { key = "pnl", label = "損益", numeric = true },
                new { key = "pnlPct", label = "損益 %", numeric = true },
            },
            rows = s.Positions.OrderByDescending(p => Math.Abs(p.MarketValueTWD)).Select(p => new Dictionary<string, object?>
            {
                // One column for both, so the table fits the assistant's sheet.
                ["name"] = string.IsNullOrEmpty(p.Symbol) || p.Symbol == p.Name ? p.Name : $"{p.Name}（{p.Symbol}）",
                ["value"] = Math.Round(p.MarketValueTWD),
                ["pnl"] = p.UnrealizedPnlTWD is { } pnl ? Math.Round(pnl) : null,
                ["pnlPct"] = p.UnrealizedPnlPct is { } pct ? Math.Round(pct, 2) : null,
            }).ToList(),
        };

        private static string Money(decimal v) => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);

        private static string Signed(decimal v) => (v >= 0 ? "+" : "-") + Money(Math.Abs(v));

        private static string Pct(decimal v) => (v >= 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture) + "%";
    }
}
