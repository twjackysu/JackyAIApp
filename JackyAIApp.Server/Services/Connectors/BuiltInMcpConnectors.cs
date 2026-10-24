namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>A remote MCP server in the catalog, added with one click (docs/ai-native/design/03 §6).</summary>
    /// <param name="Key">Stable, lower-case kebab; also the installation's <c>DefinitionKey</c>.</param>
    /// <param name="Category">site, data, dev, productivity or finance (the client's categories).</param>
    /// <param name="AuthKind">How the reader signs in: none, or oauth (MCP authorization, <see cref="IMcpOAuthFlow"/>).</param>
    public sealed record McpConnectorDescriptor(
        string Key, string DisplayName, string Description, string Category, string Publisher, string Url,
        string AuthKind, string? DocsUrl);

    /// <summary>
    /// The catalog's remote MCP servers. Adding one is a line here: <c>BuiltInMcpConnectorsTests</c> checks
    /// each key is unique and plain, its URL is one <see cref="McpServerUrl"/> accepts, and nothing is missing.
    /// </summary>
    public static class BuiltInMcpConnectors
    {
        public static readonly IReadOnlyList<McpConnectorDescriptor> All =
        [
            new("tw-stock", "台股資料（TW Stock）",
                "證交所、櫃買中心、期交所、公開資訊觀測站等台灣市場資料：即時報價、歷史股價、財報、法人買賣超、ETF。Stock Intelligence 的 AI 助理用的就是它。",
                "finance", "TW-Stock-MCP-Server", TwStockMcp.Url, "none", null),
            new("deepwiki", "DeepWiki",
                "查詢 GitHub 公開專案的文件與架構，問任何開源 repo 的問題。",
                "dev", "Devin（Cognition）", "https://mcp.deepwiki.com/mcp", "none", "https://docs.devin.ai/work-with-devin/deepwiki-mcp"),
            new("notion", "Notion",
                "搜尋、閱讀與建立 Notion 頁面和資料庫。用你的 Notion 帳號登入授權，AI 只看得到你授權的工作區；建立或修改頁面前會先問你。",
                "productivity", "Notion Labs", "https://mcp.notion.com/mcp", "oauth", "https://developers.notion.com/docs/mcp"),
            new("linear", "Linear",
                "查詢、建立與更新 Linear 的 issue、專案與留言。用你的 Linear 帳號登入授權，權限與你在 Linear 的一樣；寫入前會先問你。",
                "productivity", "Linear", "https://mcp.linear.app/mcp", "oauth", "https://linear.app/docs/mcp"),
        ];

        public static McpConnectorDescriptor? Find(string key) => All.FirstOrDefault(d => d.Key == key);
    }
}
