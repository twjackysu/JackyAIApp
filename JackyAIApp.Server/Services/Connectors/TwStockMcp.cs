namespace JackyAIApp.Server.Services.Connectors
{
    /// <summary>
    /// The tw-stock remote MCP server (Taiwan market data), as the AI assistants attach it: through
    /// Anthropic's MCP connector, which calls it from Anthropic's servers, not ours. That suits it —
    /// it is read-only, needs no sign-in and is trusted — and is why it is not run as one of our own
    /// tools (docs/ai-native/design/03, "AnthropicHosted").
    /// </summary>
    public static class TwStockMcp
    {
        /// <summary>The server's name in a request (<c>mcp_servers[].name</c>, the toolset's <c>mcp_server_name</c>).</summary>
        public const string Name = "tw-stock";

        public const string Url = "https://TW-Stock-MCP-Server.fastmcp.app/mcp";

        /// <summary>The beta that turns on Anthropic's MCP connector.</summary>
        public const string ConnectorBeta = "mcp-client-2025-11-20";

        /// <summary>
        /// The tools whose definitions go with every request; the server's other tools (177 in all,
        /// ~53K tokens of definitions) are deferred and found with the regex tool search when a
        /// question needs one. Measured 2026-10-22 on 8 questions: same answers, ~133K → ~21K
        /// context tokens a question. Keyed by the server's own tool name, no server prefix. A guess
        /// at the most asked-for ones — no record of which tools are called was kept before; the turn
        /// log now names them (see AnthropicChatService.StreamChatAsync), so revisit this list from there.
        /// </summary>
        public static readonly IReadOnlyList<string> AlwaysLoadedTools =
        [
            "get_realtime_quote",
            "get_stock_history",
            "get_technical_indicators",
            "get_twse_institutional_investors_summary",
            "get_company_monthly_revenue",
        ];
    }
}
