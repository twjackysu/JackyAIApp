using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.SpacedRepetition;

namespace JackyAIApp.Server.Services.Dictionary.Assistant
{
    /// <summary>
    /// The English app's ready-made requests for another AI using the site's MCP server (<see cref="SitePrompt"/>): a
    /// word explained from the site's dictionary, a review quiz asked one question at a time, and a passage written with
    /// the reader's own saved words.
    /// </summary>
    public static class EnglishSitePrompts
    {
        public static readonly SitePrompt ExplainWord = new(
            "explain_word",
            "解釋單字",
            "從 Jacky Studio 的字典查一個英文單字，用中文解釋並附例句。",
            [LookUpWordTool.Name],
            [new SitePromptArgument("word", "要查的英文單字，例如 serendipity", Required: true)],
            args =>
                $"用 jacky-studio 的 {LookUpWordTool.Name} 查「{args["word"]}」，用繁體中文說明它最常用的一兩個意思，" +
                "每個意思附一個英文例句與中文翻譯，再說一個容易跟它搞混的字和差別。只根據工具查到的內容，查不到就直說。");

        public static readonly SitePrompt QuizMe = new(
            "quiz_me",
            "考我單字",
            "用我該複習的單字出選擇題，一次問一題。",
            [ReviewQuizTool.Name],
            [new SitePromptArgument("count", $"題數（1–{ReviewQuizService.MaxQuestions}），預設 {ReviewQuizTool.DefaultCount}")],
            args =>
                $"用 jacky-studio 的 {ReviewQuizTool.Name}" +
                (CountOf(args) is { } count ? $"（count：{count}）" : "") +
                " 出我該複習的單字的選擇題。一次只給我一題（題目和選項），等我回答後再說對錯與一句解說，" +
                "然後才出下一題；全部答完告訴我答對幾題、哪些字要再加強。");

        public static readonly SitePrompt WriteWithMyWords = new(
            "write_with_my_words",
            "用我的單字寫短文",
            "用我最近存進單字庫的字寫一段英文短文，附中文翻譯。",
            [SavedWordsTool.Name],
            [new SitePromptArgument("topic", "短文的主題或情境（選填），例如「出差」")],
            args =>
                $"用 jacky-studio 的 {SavedWordsTool.Name} 讀我最近存的單字，挑 5 個寫一段約 120 字的英文短文" +
                (args.TryGetValue("topic", out var topic) ? $"，主題是「{topic}」" : "") +
                "，把用到的單字用粗體標出，最後附上中文翻譯。");

        /// <summary>The quiz's number of questions when the reader gave a number (kept within what the tool asks), else null.</summary>
        internal static int? CountOf(IReadOnlyDictionary<string, string> args) =>
            args.TryGetValue("count", out var text) && int.TryParse(text, out var count)
                ? Math.Clamp(count, 1, ReviewQuizService.MaxQuestions)
                : null;

        public static IReadOnlyList<SitePrompt> All { get; } = [ExplainWord, QuizMe, WriteWithMyWords];
    }
}
