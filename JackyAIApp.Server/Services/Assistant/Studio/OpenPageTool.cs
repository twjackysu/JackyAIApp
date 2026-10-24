using System.Text.Json;
using System.Text.RegularExpressions;
using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Assistant.Studio
{
    /// <summary>
    /// Gives the reader a link to one of the site's pages (a word's dictionary page included), shown as a
    /// card they can follow. It only reads: nothing changes, and the reader decides whether to go.
    /// </summary>
    public sealed partial class OpenPageTool : IAiTool
    {
        public const string Name = "open_page";

        internal const string UnknownPageMessage = "沒有這個頁面；page 必須是列出的其中一個。";

        public static readonly AiToolDescriptor Definition = new(
            Name,
            "開啟頁面",
            "給使用者本網站某個頁面的連結（在對話中顯示成可點的卡片）。使用者想做的事有對應的頁面時使用，例如查單字就給 dictionary 加上 word。" +
            "可用的頁面：" + string.Join("；", SitePages.All.Select(p => $"{p.Key}＝{p.Title}（{p.Description}）")),
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    page = new { type = "string", @enum = SitePages.All.Select(p => p.Key).ToArray(), description = "要開的頁面" },
                    word = new { type = "string", description = "page 是 dictionary 時要查的英文單字（可省略）" },
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

            return Task.FromResult(new AiToolResult(
                $"已在對話中給使用者「{title}」的連結（{href}）。",
                Card: new { kind = "link", title, href, description = page.Description, actionLabel = "前往" }));
        }
    }
}
