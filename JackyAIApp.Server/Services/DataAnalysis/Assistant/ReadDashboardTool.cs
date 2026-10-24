using System.Text;
using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>
    /// One of the reader's own dashboards as it is now, for Studio AI ("這個 dashboard 最近有什麼變化？"): each chart's
    /// title, kind and rows, re-run as the dashboard page does (<see cref="IDashboardService.GetWidgetDataAsync"/>), as text
    /// for the model, and a link card to the dashboard. Only a dashboard the reader owns: one shared with them holds
    /// someone else's titles and data, words the model would read as if they were the reader's, so it is left to the page.
    /// A read: nothing changes, no usage is charged (the widgets' queries are the page's own, run without an LLM).
    /// </summary>
    public sealed class ReadDashboardTool(IDashboardService dashboards) : ISiteTool
    {
        public const string Name = "read_dashboard";

        /// <summary>The most charts read, in the page's order (top to bottom, left to right): each is a query.</summary>
        internal const int MaxWidgets = 6;

        /// <summary>The most rows of a chart the model is told.</summary>
        internal const int MaxRowsPerWidget = 40;

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "讀取 dashboard",
            "讀取使用者自己的一個 dashboard 現在的樣子：每張圖的標題、圖表類型與用最新資料重算的數字。" +
            "使用者問某個 dashboard 的內容、趨勢、異常，或要你解讀、摘要它時使用。" +
            "dashboard 傳它的 id（使用者正在看的那個，系統提示會給）或名稱。只能讀使用者自己建立的 dashboard。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    dashboard = new { type = "string", description = "dashboard 的 id 或名稱，例如「業績追蹤」" },
                },
                required = new[] { "dashboard" },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var asked = Text(input, "dashboard")?.Trim();
            if (string.IsNullOrEmpty(asked)) return AiToolResult.Error("請提供 dashboard 的 id 或名稱。");

            if (!Guid.TryParse(asked, out var id))
            {
                var listed = await dashboards.ListAsync(ct);
                if (!listed.IsSuccess) return AiToolResult.Error("讀不到使用者的 dashboard，請稍後再試。");
                var owned = listed.Data!.Where(d => d.IsOwner).ToList();
                var named = owned.FirstOrDefault(d => string.Equals(d.Name, asked, StringComparison.OrdinalIgnoreCase));
                if (named == null)
                    return AiToolResult.Error(owned.Count == 0
                        ? "使用者還沒有自己的 dashboard：可以在資料分析頁請 AI 畫圖並存成 dashboard。"
                        : $"找不到名為「{asked}」的 dashboard。使用者自己的 dashboard：{string.Join("、", owned.Select(d => $"「{d.Name}」"))}。");
                id = named.Id;
            }

            var got = await dashboards.GetAsync(id, ct);
            if (!got.IsSuccess) return AiToolResult.Error("找不到這個 dashboard，或使用者不能看它。");
            var dashboard = got.Data!;
            if (!dashboard.IsOwner)
                return AiToolResult.Error("這個 dashboard 是別人分享給使用者的，只能讀使用者自己建立的 dashboard；請他直接看頁面上的圖。");

            var text = new StringBuilder();
            var widgets = dashboard.Widgets;
            text.AppendLine($"Dashboard「{dashboard.Name}」，共 {widgets.Count} 張圖（依頁面上由上而下的順序）。數字是現在用最新資料重算的結果：");
            if (widgets.Count == 0) text.AppendLine("（還沒有任何圖。可以在資料分析頁請 AI 畫圖後存進來。）");
            foreach (var widget in widgets.Take(MaxWidgets))
            {
                text.AppendLine();
                text.AppendLine($"## {(string.IsNullOrWhiteSpace(widget.Title) ? "未命名的圖" : widget.Title)}（{widget.VizType}）");
                var data = await dashboards.GetWidgetDataAsync(id, widget.Id, ct);
                if (!data.IsSuccess)
                {
                    text.AppendLine("這張圖的資料現在讀不到。");
                    continue;
                }
                var rows = data.Data!.Rows;
                text.AppendLine(rows.Count > MaxRowsPerWidget
                    ? $"共 {rows.Count} 列，以下是前 {MaxRowsPerWidget} 列："
                    : $"共 {rows.Count} 列：");
                text.AppendLine(AiJson.Serialize(rows.Take(MaxRowsPerWidget).ToList()));
            }
            if (widgets.Count > MaxWidgets)
                text.AppendLine().AppendLine($"…另有 {widgets.Count - MaxWidgets} 張圖沒有讀取，請使用者直接打開 dashboard 看。");

            return new AiToolResult(text.ToString().TrimEnd(), Card: new
            {
                kind = "link",
                title = dashboard.Name,
                href = $"/dashboards/{dashboard.Id}",
                description = $"{widgets.Count} 張圖，剛用最新資料重算",
                actionLabel = "打開 dashboard",
            });
        }

        private static string? Text(JsonElement input, string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
