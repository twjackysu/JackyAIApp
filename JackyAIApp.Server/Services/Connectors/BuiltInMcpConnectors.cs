namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>A remote MCP server in the catalog, added with one click (docs/ai-native/design/03 §6).</summary>
    /// <param name="Key">Stable, lower-case kebab; also the installation's <c>DefinitionKey</c>.</param>
    /// <param name="Category">site, data, dev, productivity or finance (the client's categories).</param>
    /// <param name="AuthKind">How the reader signs in: only none for now (no OAuth yet, design/07 M3).</param>
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
        ];

        public static McpConnectorDescriptor? Find(string key) => All.FirstOrDefault(d => d.Key == key);
    }
}
