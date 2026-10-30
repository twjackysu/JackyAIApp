using System.Globalization;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.Portfolio;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Portfolio.Assistant
{
    /// <summary>
    /// One holding's new quantity, for Studio AI ("我今天加碼 0050 兩張，均價 185，幫我記下來"): the latest snapshot with
    /// that one holding changed, saved as today's snapshot the way /portfolio saves one — so the history keeps the
    /// earlier snapshot, and a second change today replaces today's. A write, so the reader is asked first.
    /// <para>
    /// Only a holding already in the latest snapshot: a new one needs its asset type, market and currency, which the
    /// holdings editor on /portfolio looks up properly. The cost basis follows the average-cost rule: selling keeps the
    /// average cost (the cost shrinks in proportion), buying adds what was paid — so buying more of a holding that has a
    /// cost needs what was paid (the price, or the total in the cost's currency when that is not the price's), or its
    /// P&amp;L would be wrong from then on.
    /// </para>
    /// </summary>
    public sealed class UpdateHoldingTool(IPortfolioService portfolio, TimeProvider time) : ISiteTool, IDescribesCall
    {
        public const string Name = "update_holding";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "更新持倉",
            "更新使用者投資組合裡一筆既有持倉的數量（股數、基金單位數，現金、負債、不動產是金額），存成今天的快照。" +
            "使用者說他買進、賣出、加碼、減碼，或要你把某筆持倉改成多少時使用。quantity（改成多少）與 change（增減多少，賣出是負數）" +
            "擇一；台股一張是 1000 股。買進有成本的持倉時要給 price（這次的成交價，每單位）或 cost（這次總共付了多少），使用者沒說就先問他；" +
            "價格與成本幣別不同的持倉（例如用台幣買的美股）只收 cost（用成本的幣別），工具會告訴你是哪個幣別。" +
            "只能改已經有的持倉，新增持倉請他到投資組合頁面。執行前會請使用者確認。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    symbol = new { type = "string", description = "持倉的代號或名稱（例如 0050、AAPL、台積電；現金、負債用名稱）" },
                    quantity = new { type = "number", minimum = 0, description = "改成多少；0 代表從投資組合移除" },
                    change = new { type = "number", description = "增減多少：買進是正數、賣出是負數" },
                    price = new { type = "number", exclusiveMinimum = 0, description = "買進的成交價（每單位，持倉的價格幣別）" },
                    cost = new { type = "number", exclusiveMinimum = 0, description = "這次買進總共付了多少（持倉成本的幣別）" },
                },
                required = new[] { "symbol" },
            }),
            ToolEffect.Write);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context) =>
            HoldingChange.Of(input) is { } c ? c.Describe() + "（存成今天的快照）" : null;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (HoldingChange.Of(input) is not { } change)
                return AiToolResult.Error("要給 symbol，以及 quantity（改成多少，不能是負數）或 change（增減多少）其中一個；price 要大於 0。");

            var latest = await portfolio.GetLatestHoldingsAsync(context.UserId, ct);
            var matches = latest.Positions.Where(p => change.Matches(p)).ToList();
            if (matches.Count == 0)
            {
                var held = string.Join("、", latest.Positions.Select(Label).Take(20));
                return new AiToolResult(
                    latest.Positions.Count == 0
                        ? "使用者還沒有在本站記錄任何持倉，所以沒有更新。請他到投資組合頁面輸入第一份持倉。"
                        : $"使用者的持倉裡沒有「{change.Symbol}」，所以沒有更新（只能改既有的持倉，新增請到投資組合頁面）。目前的持倉：{held}。",
                    IsError: true,
                    Card: PortfolioCard("新增持倉請到投資組合頁面", "打開投資組合"));
            }
            if (matches.Count > 1)
                return AiToolResult.Error(
                    $"「{change.Symbol}」對到不只一筆持倉（{string.Join("、", matches.Select(Label))}），所以沒有更新。請使用者說清楚是哪一筆，或到投資組合頁面修改。");

            var position = matches[0];
            var before = position.Quantity;
            var after = change.Quantity ?? before + change.Change!.Value;
            if (after < 0)
                return AiToolResult.Error($"{Label(position)} 目前只有 {Amount(before)}，賣出 {Amount(-change.Change!.Value)} 會變成負數，所以沒有更新。");
            if (after == before)
                return new AiToolResult($"{Label(position)} 本來就是 {Amount(before)}，沒有變更。", Card: PortfolioCard($"{Label(position)}：{Amount(before)}", "看投資組合"));

            var costBefore = position.CostBasis;
            var costCurrency = string.IsNullOrWhiteSpace(position.CostCurrency) ? position.Currency : position.CostCurrency;
            if (AssetValuationPolicy.For(position.AssetType).HasCostBasis && costBefore is { } cost)
            {
                if (after < before)
                    position.CostBasis = before == 0 ? null : Math.Round(cost * after / before, 2);
                else if (change.Cost is { } paid)
                    position.CostBasis = Math.Round(cost + paid, 2);
                else if (change.Price is { } price && costCurrency == position.Currency)
                    position.CostBasis = Math.Round(cost + (after - before) * price, 2);
                else if (change.Price is not null)
                    // A price is in the holding's own currency; its cost is kept in another (a US stock paid for in
                    // TWD), and converting is not the model's to guess.
                    return AiToolResult.Error(
                        $"{Label(position)} 的價格是 {position.Currency}、成本記成 {costCurrency}，所以沒有用成交價更新。請問使用者這次總共付了多少 {costCurrency}，再用 cost 傳。");
                else
                    return AiToolResult.Error(
                        $"{Label(position)} 有記錄成本，買進要知道付了多少才能算對損益，所以還沒有更新。請問使用者這次每單位的成交價（{position.Currency}）" +
                        (costCurrency == position.Currency ? "或總共付了多少。" : $"，或總共付了多少 {costCurrency}（用 cost 傳）。"));
            }
            position.Quantity = after;

            // Today's snapshot, or the latest one's date if that is later (a snapshot entered ahead of time): a change
            // saved under an earlier date would sit behind it and never show.
            var today = time.GetTaipeiToday();
            var date = latest.EffectiveDate is { } d && d > today ? d : today;
            var saved = await portfolio.SaveHoldingsAsync(context.UserId, new SaveHoldingsRequest
            {
                EffectiveDate = date,
                // A quantity of 0 drops the row (AssetValuationPolicy.Normalize), as the editor's empty rows are.
                Positions = latest.Positions,
            }, ct);
            // Beyond HoldingsLimits (a new date with the snapshots full): the service's own words, nothing saved.
            if (!saved.IsSuccess)
                return AiToolResult.Error($"沒有更新 {Label(position)}：{saved.ErrorMessage}");

            var dateText = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // A snapshot already on that date is replaced, as the editor's save replaces it: say so, not that history keeps it.
            var replacedSameDay = latest.EffectiveDate == date;
            var text = after == 0
                ? $"已把 {Label(position)} 從投資組合移除（原本 {Amount(before)}），存成 {dateText} 的快照" +
                  (replacedSameDay ? "（取代了這一天原本的快照）。" : "（之前的快照留在歷史裡）。")
                : $"已更新 {Label(position)}：{Amount(before)} → {Amount(after)}" +
                  (position.CostBasis != costBefore && position.CostBasis is { } costAfter
                      ? $"，成本 {Amount(costBefore ?? 0)} → {Amount(costAfter)} {costCurrency}"
                      : "") +
                  $"，存成 {dateText} 的快照" + (replacedSameDay ? "（取代了這一天原本的快照）。" : "（之前的快照留在歷史裡）。");
            return new AiToolResult(
                text,
                Card: PortfolioCard(after == 0 ? $"{Label(position)} 已移除" : $"{Label(position)}：{Amount(before)} → {Amount(after)}", "看投資組合"));
        }

        private static string Label(PositionInput p) =>
            string.IsNullOrWhiteSpace(p.Symbol) || string.Equals(p.Symbol, p.Name, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(p.Name) ? p.AssetType.ToString() : p.Name)
                : string.IsNullOrWhiteSpace(p.Name) ? p.Symbol : $"{p.Name}（{p.Symbol}）";

        internal static string Amount(decimal v) => v.ToString("#,0.####", CultureInfo.InvariantCulture);

        private static object PortfolioCard(string description, string actionLabel) => new
        {
            kind = "link",
            title = "投資組合",
            href = "/portfolio",
            description,
            actionLabel,
        };
    }

    /// <summary>What one <see cref="UpdateHoldingTool"/> call asks for: the holding, and either its new quantity or the change.</summary>
    internal sealed record HoldingChange(string Symbol, decimal? Quantity, decimal? Change, decimal? Price, decimal? Cost)
    {
        internal const int MaxSymbolLength = 40;
        /// <summary>The largest quantity, change, price or cost taken: a trillion.</summary>
        internal const decimal MaxNumber = 1_000_000_000_000m;

        public static HoldingChange? Of(JsonElement input)
        {
            if (input.ValueKind != JsonValueKind.Object) return null;
            var symbol = input.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(symbol) || symbol.Length > MaxSymbolLength) return null;

            var quantity = NumberOf(input, "quantity");
            var change = NumberOf(input, "change");
            var price = NumberOf(input, "price");
            var cost = NumberOf(input, "cost");
            // Exactly one of the two; a change of nothing is not a change.
            if ((quantity is null) == (change is null) || quantity < 0 || change == 0 || price <= 0 || cost <= 0) return null;
            // Beyond what a holding can be (the column is decimal(18,6)), and before `before + change` could overflow (D-241).
            if (new[] { quantity, change, price, cost }.Any(n => n is { } v && Math.Abs(v) > MaxNumber)) return null;
            return new HoldingChange(symbol, quantity, change, price, cost);
        }

        /// <summary>By symbol or by name ("0050" or "元大台灣50"); two holdings that both match are refused, not guessed.</summary>
        public bool Matches(PositionInput p) =>
            string.Equals(p.Symbol?.Trim(), Symbol, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name?.Trim(), Symbol, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The card's words. What was paid is said wherever it may be added to the cost: a new quantity that turns out larger
        /// adds it too (D-241), so the reader never allows an amount the card did not show.
        /// </summary>
        public string Describe() => (Quantity, Change) switch
        {
            (0m, _) => $"把「{Symbol}」從你的投資組合移除",
            ({ } q, _) => $"把「{Symbol}」的數量改成 {UpdateHoldingTool.Amount(q)}" + Paid(),
            (_, > 0m) => $"「{Symbol}」加 {UpdateHoldingTool.Amount(Change!.Value)}" + Paid(),
            _ => $"「{Symbol}」減 {UpdateHoldingTool.Amount(-Change!.Value)}",
        };

        private string Paid() =>
            Cost is { } c ? $"（共付 {UpdateHoldingTool.Amount(c)}）" : Price is { } p ? $"（成交價 {UpdateHoldingTool.Amount(p)}）" : "";

        private static decimal? NumberOf(JsonElement input, string name) =>
            input.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;
    }
}
