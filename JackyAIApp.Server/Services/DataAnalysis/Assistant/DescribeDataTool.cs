using System.Text.Json;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>
    /// What the data on screen holds: its columns, their types, and how far its date columns reach —
    /// what the page's Schema tab shows. Lets the assistant answer 「有哪些欄位」 and word its
    /// <see cref="AskDataTool"/> questions in the data's own terms. No AI call, so nothing is charged.
    /// </summary>
    public sealed class DescribeDataTool(IDataAnalysisQueryService queries, ISessionDataStore sessions, DataAnalysisPage page) : IAiTool
    {
        public const string Name = "describe_data";

        /// <summary>How many date columns get their latest date read (a query each on a source).</summary>
        internal const int MaxDateColumns = 3;

        private static readonly HashSet<string> DateTypes = ["DateTime", "DateTimeOffset", "Date"];

        /// <summary>The tool as the model and the reader see it (also listed on the connectors page).</summary>
        public static readonly AiToolDescriptor Definition = new(
            Name,
            "讀取資料結構",
            "列出使用者畫面上這份資料的欄位名稱與型別，以及日期欄位的最新日期。不確定有哪些欄位、或使用者問資料內容時先呼叫。",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            if (!page.HasData) return AiToolResult.Error(AskDataTool.NoDataMessage);

            List<(string Name, string Type)> fields;
            if (page.SessionId != null)
            {
                var schema = sessions.GetSchema(page.SessionId);
                if (!schema.IsSuccess) return AiToolResult.Error($"讀取失敗：{schema.ErrorMessage}") with { OutsideText = true };
                fields = schema.Data!.Fields.Select(f => (f.Name, f.CanonicalType)).ToList();
            }
            else
            {
                var schema = await queries.GetSchemaAsync(page.QueryContext, ct);
                if (!schema.IsSuccess) return AiToolResult.Error($"讀取失敗：{schema.ErrorMessage}");
                fields = schema.Data!.Fields.Select(f => (f.Name, f.CanonicalType.ToString())).ToList();
            }

            var latest = new Dictionary<string, string?>();
            foreach (var (name, _) in fields.Where(f => DateTypes.Contains(f.Type)).Take(MaxDateColumns))
                latest[name] = await LatestDateAsync(name, ct);

            return new AiToolResult(AiJson.Serialize(new
            {
                columns = fields.Select(f => new { name = f.Name, type = f.Type }),
                latestDates = latest,
            }), OutsideText: page.SessionId != null); // an uploaded file's headers are its maker's words (D-163)
        }

        /// <summary>A date column's latest value as yyyy-MM-dd, or null when it has none or cannot be read.</summary>
        private async Task<string?> LatestDateAsync(string field, CancellationToken ct)
        {
            if (page.SessionId != null)
            {
                var result = sessions.GetMaxDate(page.SessionId, field);
                return result.IsSuccess && result.Data!.MaxDate is { } iso && iso.Length >= 10 ? iso[..10] : null;
            }
            var max = await queries.GetMaxDateAsync(page.QueryContext, field, ct);
            return max.IsSuccess ? max.Data!.MaxDate?.ToString("yyyy-MM-dd") : null;
        }
    }
}
