using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.DTO.DataAnalysis;
using JackyAIApp.Server.Services.Assistant;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>
    /// The Data Analysis assistant's way into the data on screen: a question in plain language,
    /// answered by the page's own 問 AI pipeline (<see cref="INlSummarizeService"/> — a plan checked
    /// against the data, its one correction, the unmatched conditions; docs/ai-native D-007), so the
    /// assistant gets the same checked answer the page gives, never a query of its own making. Each
    /// call is charged as a 問 AI question. The reader sees the result as a chart card they can add to
    /// a dashboard; the model is told the chart's id (<see cref="DataAnalysisCharts"/>), so it can save it
    /// there too (<see cref="SaveToDashboardTool"/>).
    /// </summary>
    public sealed class AskDataTool(
        INlSummarizeService nlSummarize, IUsageLimitService usageLimits, DataAnalysisPage page, DataAnalysisCharts charts) : IAiTool
    {
        public const string Name = "ask_data";

        /// <summary>The most rows the model reads; the card shows as many as the client draws.</summary>
        internal const int MaxRowsForModel = 100;
        internal const int MaxRowsForCard = 500;

        /// <summary>The charts the model may pick, as the client's viz ids.</summary>
        internal static readonly string[] Charts =
            ["column", "bar", "line", "pie", "donut", "table", "kpi", "stackedColumn", "stackedBar", "treemap", "waterfall"];

        internal const string DefaultChart = "column";
        internal const string NoDataMessage = "這個頁面目前沒有選定資料源，請使用者先在頁面上選擇或上傳資料。";
        internal const string LimitMessage = "使用者今天的 AI 額度已用完，請告訴使用者明天（台北時間 08:00 重置）再試。";
        internal const string NoQuestionMessage = "請提供 question：要問這份資料的問題。";

        /// <summary>The tool as the model and the reader see it (also listed on the connectors page).</summary>
        public static readonly AiToolDescriptor Definition = new(
            Name,
            "查詢資料",
            "用自然語言查詢使用者畫面上這份資料（彙總、分組、篩選、排行、趨勢），回傳彙總後的資料列，並在對話中畫成圖表。" +
            "一次問一件事；需要比較或拆解時可以分幾次呼叫。結果裡的 notes／unmatched 是沒能套用的條件，回答時要告訴使用者。",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    question = new { type = "string", description = "要問這份資料的問題，用使用者的語言，例如「2025 年各月營收」" },
                    chart = new { type = "string", @enum = Charts, description = "結果要畫成的圖：趨勢用 line，分類比較用 column 或 bar，占比用 pie，單一數字用 kpi，明細用 table" },
                    title = new { type = "string", description = "圖表標題（繁體中文，簡短）" },
                },
                required = new[] { "question" },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var question = Text(input, "question");
            if (string.IsNullOrWhiteSpace(question)) return AiToolResult.Error(NoQuestionMessage);
            if (!page.HasData) return AiToolResult.Error(NoDataMessage);
            if (!await usageLimits.TryConsumeAsync(context.UserId, UsageFeature.NlSummarize))
                return AiToolResult.Error(LimitMessage);

            var result = await nlSummarize.SummarizeAsync(new NlSummarizeRequestDto
            {
                Context = page.QueryContext,
                SessionId = page.SessionId,
                Query = question,
            }, ct);
            if (!result.IsSuccess)
                // The pipeline's reason can quote the file's columns and values (D-163).
                return AiToolResult.Error($"查詢失敗：{result.ErrorMessage}") with { OutsideText = page.SessionId != null };

            var answer = result.Data!;
            var rows = answer.Rows.Select(ToRow).ToList();
            var chart = Text(input, "chart") is { } c && Charts.Contains(c) ? c : DefaultChart;
            var title = Text(input, "title") ?? answer.Interpretation;
            // A session (an uploaded file) lasts only as long as the reader's visit, so a plan over one
            // cannot become a dashboard widget.
            var plan = page.SessionId == null ? answer.Plan with { Context = page.QueryContext } : null;
            var chartId = plan == null ? null : charts.Remember(context, new RememberedChart(plan, chart, title));
            var content = AiJson.Serialize(new
            {
                chartId,
                interpretation = answer.Interpretation,
                notes = answer.Notes,
                unmatched = answer.Unmatched,
                rowCount = rows.Count,
                rows = rows.Take(MaxRowsForModel),
                rowsCut = rows.Count > MaxRowsForModel,
            });
            // An uploaded file's cells are whatever its maker wrote (D-163); a source's are the reader's own database (D-156).
            return new AiToolResult(content, Card: ChartCard(answer, rows, chart, title, plan), OutsideText: page.SessionId != null);
        }

        /// <summary>
        /// The card the client draws (DataAnalysis/assistant/chartCard.ts): the rows, which columns are
        /// group-bys and dates (as the page works them out for its own 問 AI), and the plan with the
        /// page's source, which is what a dashboard widget stores.
        /// </summary>
        private static object ChartCard(
            NlSummarizeResponseDto answer, List<Dictionary<string, object?>> rows, string chart, string? title, SummarizeRequestDto? plan)
        {
            var groups = answer.Plan.ViewBy.Select(g => g.Alias ?? g.FieldName).ToList();
            var headers = rows.Count > 0
                ? rows[0].Keys.ToList()
                : [.. groups, .. answer.Plan.Measures.Select(m => m.Alias ?? m.FieldName)];
            return new
            {
                kind = "chart",
                title,
                data = new
                {
                    viz = chart,
                    headers,
                    rows = rows.Take(MaxRowsForCard),
                    groupHints = groups,
                    temporalHints = answer.Plan.ViewBy.Where(g => g.Type == "DateTrunc").Select(g => g.Alias ?? g.FieldName),
                    plan,
                },
            };
        }

        /// <summary>One result row as plain values (an engine row and a session row are both dictionaries).</summary>
        private static Dictionary<string, object?> ToRow(object row) => row switch
        {
            IReadOnlyDictionary<string, object?> r => r.ToDictionary(p => p.Key, p => p.Value),
            IDictionary<string, object?> r => r.ToDictionary(p => p.Key, p => p.Value),
            _ => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(row)) ?? [],
        };

        private static string? Text(JsonElement input, string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>
    /// The data on the reader's screen, from the page context the client sends: a saved or default
    /// source (<c>sourceId</c>, <c>tableName</c>) or an uploaded file's session (<c>sessionId</c>).
    /// Anything else in the context is ignored; whether the reader may read that source is checked by
    /// the query itself, as for the page.
    /// </summary>
    public sealed record DataAnalysisPage(Guid? SourceId, string? TableName, string? SessionId, bool UsesDefaultSource)
    {
        public static DataAnalysisPage From(ChatProfileContext context)
        {
            var sessionId = context.PageValue("sessionId");
            if (!string.IsNullOrWhiteSpace(sessionId)) return new(null, null, sessionId, false);
            var sourceId = Guid.TryParse(context.PageValue("sourceId"), out var id) ? id : (Guid?)null;
            var table = context.PageValue("tableName");
            // No source named: the page's built-in default source, as the page itself queries it.
            return new(sourceId, string.IsNullOrWhiteSpace(table) ? null : table, null, sourceId == null);
        }

        public bool HasData => SessionId != null || SourceId != null || UsesDefaultSource;

        /// <summary>The source as the page's own queries name it (the default source may name its table too).</summary>
        public QueryContextDto? QueryContext =>
            SessionId != null || (SourceId == null && TableName == null)
                ? null
                : new QueryContextDto { SourceId = SourceId, TableName = TableName };
    }
}
