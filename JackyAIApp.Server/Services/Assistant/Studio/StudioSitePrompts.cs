using JackyAIApp.Server.Services.Connectors;
using JackyAIApp.Server.Services.Connectors.Google;

namespace JackyAIApp.Server.Services.Assistant.Studio
{
    /// <summary>
    /// The morning briefing as a ready-made request for another AI using the site's MCP server (<see cref="SitePrompt"/>):
    /// what the home page's 「AI 今日簡報」 asks Studio AI, worded for a client that only has the tools. Its tools belong to
    /// other modules, so they are named as text here; <c>SitePromptDefinitionsTests</c> checks each is a site tool.
    /// </summary>
    public static class StudioSitePrompts
    {
        public static readonly SitePrompt DailyBriefing = new(
            "daily_briefing",
            "今日簡報",
            "一次讀我的行程、學習進度與投資組合，整理成今天的三段重點。",
            [GoogleCalendarTool.Name, "learning_progress", "portfolio_summary"],
            [],
            _ =>
                $"給我今天的簡報。在同一次回覆裡呼叫 jacky-studio 的 {GoogleCalendarTool.Name}（今天）、learning_progress、portfolio_summary，" +
                "整理成「📅 今天的行程」「📚 學習」「💰 資產」三段，每段一兩句重點（行程列出時間與空檔，學習說要複習幾個字與連續天數，" +
                "資產說淨值與今天最需要注意的一檔），最後一句建議我先做哪件事。某個工具沒有資料（沒連 Google、還沒有投資組合）" +
                "就在那段用一句話說明怎麼開始，不要編造。");

        public static IReadOnlyList<SitePrompt> All { get; } = [DailyBriefing];
    }
}
