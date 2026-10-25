using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Assistant.Studio
{
    /// <summary>
    /// Gives the reader a link to one of the site's pages (a word's dictionary page included), shown as a
    /// card they can follow; when they asked to be taken there (<c>go</c>), their page also goes there at once
    /// (<see cref="AiToolResult.Navigate"/>). Going to a page with an assistant of its own (<see cref="HandsOverTo"/>), it can
    /// also hand that assistant the reader's task (<c>ask</c>, <see cref="AiToolResult.Handoff"/>): the page asks it there, so
    /// a dashboard asked for in Studio AI is made by /data-analysis's assistant, which has the data tools. It only reads:
    /// nothing changes, and only the site's own pages are reached.
    /// </summary>
    public sealed partial class OpenPageTool : IAiTool
    {
        public const string Name = "open_page";

        internal const string UnknownPageMessage = "沒有這個頁面；page 必須是列出的其中一個。";

        /// <summary>The pages with an assistant of their own, which a task can be handed to (<c>ask</c>).</summary>
        public static readonly IReadOnlySet<string> HandsOverTo = new HashSet<string>(StringComparer.Ordinal) { "finance", "data-analysis" };

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "開啟頁面",
            "給使用者本網站某個頁面的連結（在對話中顯示成可點的卡片）。使用者想做的事有對應的頁面時使用，例如查單字就給 dictionary 加上 word。" +
            "使用者明確要你帶他過去或打開某頁（「帶我去」「打開」「我要去」）時把 go 設成 true，畫面會直接切換到那一頁（對話保留）；只是推薦時不要設。" +
                "page 是 finance 或 data-analysis 時，那一頁有自己的 AI 助理：使用者要你做只有那裡的助理做得到的事時，同時設 go 與 ask（要交給它的問題），" +
                "頁面會切過去並由那裡的助理接手回答。" +
            "可用的頁面：" + string.Join("；", SitePages.All.Select(p => $"{p.Key}＝{p.Title}（{p.Description}）")),
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    page = new { type = "string", @enum = SitePages.All.Select(p => p.Key).ToArray(), description = "要開的頁面" },
                    word = new { type = "string", description = "page 是 dictionary 時要查的英文單字（可省略）" },
                    go = new { type = "boolean", description = "使用者要你現在就帶他過去時為 true（畫面直接切換）" },
                    ask = new
                    {
                        type = "string",
                        description = "page 是 finance 或 data-analysis、go 為 true 時，交給那一頁 AI 助理的問題：照使用者的意思寫成一句完整的請求（繁體中文），" +
                            "例如「幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開」。其他頁面不用。",
                    },
                },
                required = new[] { "page" },
            }),
            ToolEffect.Read);

        public AiToolDescriptor Descriptor => Definition;

        [GeneratedRegex(@"^[A-Za-z][A-Za-z' -]{0,49}$")]
        private static partial Regex WordPattern();

        public Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
        {
            var key = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
            if (SitePages.Find(key) is not { } page) return Task.FromResult(AiToolResult.Error(UnknownPageMessage));

            var word = input.TryGetProperty("word", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString()?.Trim() : null;
            var href = page.Path;
            var title = page.Title;
            if (page.Key == "dictionary" && !string.IsNullOrEmpty(word) && WordPattern().IsMatch(word))
            {
                href = $"{page.Path}/{Uri.EscapeDataString(word.ToLowerInvariant())}";
                title = $"{word}｜字典";
            }

            var go = input.TryGetProperty("go", out var g) && g.ValueKind == JsonValueKind.True;
            var ask = go && HandsOverTo.Contains(page.Key) && input.TryGetProperty("ask", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString()?.Trim()
                : null;
            if (string.IsNullOrEmpty(ask)) ask = null;
            var content = !go
                ? $"已在對話中給使用者「{title}」的連結（{href}）。"
                : ask != null
                    ? $"已帶使用者到「{title}」（{href}），並把「{ask}」交給那一頁的 AI 助理，由它接手回答；你不用自己回答，用一句話告訴使用者那裡的助理會接著做。"
                    : $"已帶使用者到「{title}」（{href}），對話中也有連結。";
            return Task.FromResult(new AiToolResult(
                content,
                Card: new { kind = "link", title, href, description = page.Description, actionLabel = "前往" },
                Navigate: go ? href : null,
                Handoff: ask));
        }
    }
}
