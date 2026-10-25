using JackyAIApp.Server.Services.Connectors;

namespace JackyAIApp.Server.Services.Portfolio.Assistant
{
    /// <summary>The portfolio's ready-made request for another AI using the site's MCP server (<see cref="SitePrompt"/>).</summary>
    public static class PortfolioSitePrompts
    {
        public static readonly SitePrompt Checkup = new(
            "portfolio_checkup",
            "投資組合健檢",
            "讀我的投資組合，說明淨值、報酬、集中度與今天最需要注意的一檔。",
            [PortfolioSummaryTool.Name],
            [],
            _ =>
                $"用 jacky-studio 的 {PortfolioSummaryTool.Name} 讀我的投資組合，用繁體中文說明：淨值與總報酬、" +
                "最大的三個部位各占多少、資產是否過度集中，以及今天最需要注意的一檔和原因。" +
                "只根據工具回傳的數字，不要編造，也不要給買賣建議。");

        public static IReadOnlyList<SitePrompt> All { get; } = [Checkup];
    }
}
