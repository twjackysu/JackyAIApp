using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>Data Analysis's ready-made request for another AI using the site's MCP server (<see cref="SitePrompt"/>).</summary>
    public static class DataAnalysisSitePrompts
    {
        public static readonly SitePrompt ReadDashboard = new(
            "read_my_dashboard",
            "解讀 dashboard",
            "用最新資料讀我的一個 dashboard，說出最重要的變化與異常。",
            [ReadDashboardTool.Name],
            [new SitePromptArgument("dashboard", "dashboard 的名稱，例如「業績追蹤」", Required: true)],
            args =>
                $"用 jacky-studio 的 {ReadDashboardTool.Name} 讀我的「{args["dashboard"]}」dashboard（用最新資料），" +
                "用繁體中文說出最重要的三個變化或異常，每一點附上具體數字和它出自哪張圖；沒讀到的圖要說。");

        public static IReadOnlyList<SitePrompt> All { get; } = [ReadDashboard];
    }
}
