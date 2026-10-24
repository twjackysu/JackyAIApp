using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Connectors;
using Microsoft.Extensions.Caching.Memory;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>A chart <see cref="AskDataTool"/> showed, as a dashboard widget would store it.</summary>
    public sealed record RememberedChart(SummarizeRequestDto Plan, string Viz, string? Title);

    /// <summary>
    /// The charts a conversation's <c>ask_data</c> calls showed, by a short id the model is told
    /// (<c>chartId</c>), so "save that to my dashboard" saves exactly the chart the reader saw — its checked
    /// plan, never one the model writes — without asking the data again. Kept in memory for a while, per
    /// reader and conversation: an id from another conversation finds nothing.
    /// </summary>
    public sealed class DataAnalysisCharts(IMemoryCache cache)
    {
        public static readonly TimeSpan KeptFor = TimeSpan.FromHours(2);

        public string Remember(AiToolContext context, RememberedChart chart)
        {
            var id = "chart-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            cache.Set(Key(context, id), chart, KeptFor);
            return id;
        }

        public RememberedChart? Recall(AiToolContext context, string id) => cache.Get<RememberedChart>(Key(context, id));

        private static string Key(AiToolContext context, string id) => $"da-chart:{context.UserId}:{context.ConversationId}:{id}";
    }

    /// <summary>
    /// Saves a chart <c>ask_data</c> showed to one of the reader's dashboards (one of that name, or a new
    /// one): a write, so the reader is asked first. The widget stores the chart's plan with the page's
    /// source and re-runs it on every view, as one added from the chart card does.
    /// </summary>
    public sealed class SaveToDashboardTool(IDashboardService dashboards, DataAnalysisCharts charts) : IAiTool, IDescribesCall
    {
        public const string Name = "save_to_dashboard";

        internal const int MaxNameLength = 100;

        private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "存到 dashboard",
            "把 ask_data 畫出的一張圖存進使用者的 dashboard（同名的 dashboard，沒有就新建一個），之後打開 dashboard 會用最新資料重畫。" +
            "chartId 用 ask_data 結果裡的 chartId；只在使用者要求保存時使用，執行前會請使用者確認。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    chartId = new { type = "string", description = "ask_data 回傳的 chartId" },
                    dashboard = new { type = "string", description = "dashboard 名稱，例如「業績追蹤」；使用者沒說就取一個簡短的名稱" },
                    title = new { type = "string", description = "這張圖在 dashboard 上的標題（可省略，沿用圖表標題）" },
                },
                required = new[] { "chartId", "dashboard" },
            }),
            ToolEffect.Write);

        public AiToolDescriptor Descriptor => Definition;

        public string? Describe(JsonElement input, AiToolContext context)
        {
            var name = Text(input, "dashboard")?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            var chart = Text(input, "chartId") is { } id ? charts.Recall(context, id) : null;
            var title = Text(input, "title")?.Trim() is { Length: > 0 } t ? t : chart?.Title ?? "這張圖";
            return $"把「{title}」存到 dashboard「{name}」";
        }

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var chartId = Text(input, "chartId");
            var name = Text(input, "dashboard")?.Trim();
            if (string.IsNullOrEmpty(chartId) || charts.Recall(context, chartId) is not { } chart)
                return AiToolResult.Error("找不到這張圖：先用 ask_data 查詢並畫出圖表，再用它回傳的 chartId（上傳的檔案畫的圖不能存成 dashboard）。");
            if (string.IsNullOrEmpty(name)) return AiToolResult.Error("請提供 dashboard 的名稱。");
            if (name.Length > MaxNameLength) name = name[..MaxNameLength];

            var listed = await dashboards.ListAsync(ct);
            if (!listed.IsSuccess) return AiToolResult.Error("讀不到使用者的 dashboard，請稍後再試。");
            var existing = listed.Data!.FirstOrDefault(d => d.IsOwner && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            var dashboardId = existing?.Id;
            if (dashboardId == null)
            {
                var created = await dashboards.CreateAsync(new CreateDashboardRequest { Name = name }, ct);
                if (!created.IsSuccess) return AiToolResult.Error("建立 dashboard 失敗，請稍後再試。");
                dashboardId = created.Data!.Id;
            }

            var title = Text(input, "title")?.Trim() is { Length: > 0 } t ? t : chart.Title;
            var added = await dashboards.AddWidgetAsync(dashboardId.Value, new AddWidgetRequest
            {
                Title = title,
                VizType = chart.Viz,
                Plan = JsonSerializer.SerializeToElement(chart.Plan, PlanJson),
            }, ct);
            if (!added.IsSuccess) return AiToolResult.Error("加入 dashboard 失敗，請稍後再試。");

            var dashboardName = existing?.Name ?? name;
            return new AiToolResult(
                existing == null
                    ? $"已建立 dashboard「{dashboardName}」，並把「{title ?? "這張圖"}」存進去。"
                    : $"已把「{title ?? "這張圖"}」存進 dashboard「{dashboardName}」。",
                Card: new
                {
                    kind = "link",
                    title = dashboardName,
                    href = $"/dashboards/{dashboardId}",
                    description = $"「{title ?? "圖表"}」已存進這個 dashboard，打開時會用最新資料重畫",
                    actionLabel = "打開 dashboard",
                });
        }

        private static string? Text(JsonElement input, string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
