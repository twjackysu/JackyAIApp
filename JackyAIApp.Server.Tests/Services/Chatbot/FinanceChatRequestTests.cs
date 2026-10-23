using System.Text.Json;
using JackyAIApp.Server.Services.Chatbot;

namespace JackyAIApp.Server.Tests.Services.Chatbot
{
    /// <summary>
    /// Pins the whole request the Finance AI assistant sends, field by field, before the chat is
    /// split into a shared engine and per-app profiles: that refactor must leave Finance's request
    /// exactly as it is (same model, effort, tools, MCP server, system prompt and cache breakpoints),
    /// or its prompt cache and its answers change. Pieces already pinned elsewhere
    /// (<see cref="AnthropicChatServiceTests"/>, <see cref="MarketBackgroundTests"/>) are not
    /// repeated; this adds what they leave open.
    /// </summary>
    public class FinanceChatRequestTests
    {
        private static readonly DateOnly Today = new(2026, 10, 21);

        /// <summary>The system prompt as it is today, word for word.</summary>
        private const string ExpectedSystemPrompt =
            "今天是 2026-10-21（台北時間）。\n"
            + "你是JackyAIApp網站上的台股投資助手，用tw-stock工具查詢台灣市場的資料再回答。可查的範圍：\n"
            + "- 個股（上市、上櫃、興櫃）：股價與技術面、財報（損益、資產負債、現金流量，可回溯多期）、月營收、股利與除權息、法說會、重大訊息、董監與內部人持股、庫藏股\n"
            + "- 籌碼：三大法人與外資動向、融資融券、集保股權分散（大戶與散戶）、當沖、處置與注意股\n"
            + "- ETF：基本資料、折溢價、規模與受益人數、申購買回、成分股\n"
            + "- 期貨與選擇權：行情（可回溯，含盤中走勢）、三大法人與大額交易人部位、Put/Call Ratio\n"
            + "- 總體經濟：景氣燈號與領先、同時、落後指標、PMI／NMI、匯率\n"
            + "回答原則：數字以工具查到的為準，並註明資料日期；查無資料或不確定時誠實說明，不要編造。"
            + "使用者附上的檔案（報告、截圖、表格）可以一起分析。給出買賣方向的看法時，註明僅供參考、不構成投資建議。"
            + "只回答台灣金融市場、投資理財或本網站功能相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。\n"
            + "工具採延遲載入：其他工具先用 tool_search_tool_regex 搜尋，pattern 可中英文關鍵字並用（例如「外資|institutional」）。"
            + "搜到的工具在整段對話中都能直接重複呼叫，不必再搜尋。\n"
            + "回覆中的所有文字，包括呼叫工具之間的說明，都用繁體中文。";

        [Fact]
        public void TheSystemPrompt_IsWordForWordTheFinanceOne()
        {
            Assert.Equal(ExpectedSystemPrompt, AnthropicChatService.SystemPromptFor(Today));
        }

        [Fact]
        public void WithoutABackground_TheSystemPromptIsAPlainString()
        {
            var system = Body(AnthropicChatService.BuildRequest(Today, [])).GetProperty("system");

            Assert.Equal(JsonValueKind.String, system.ValueKind);
            Assert.Equal(ExpectedSystemPrompt, system.GetString());
        }

        [Fact]
        public void TheModel_ItsOutputCap_AndItsEffort_AreFinances()
        {
            var body = Body(AnthropicChatService.BuildRequest(Today, []));

            Assert.Equal("claude-haiku-5-5", body.GetProperty("model").GetString());
            Assert.Equal(32000, body.GetProperty("max_tokens").GetInt32());
            Assert.Equal("medium", body.GetProperty("output_config").GetProperty("effort").GetString());
        }

        [Fact]
        public void TheTwStockServer_IsTheOnlyMcpServer_AndTheOnlyTools_AreItsToolsetAndTheSearch()
        {
            var request = AnthropicChatService.BuildRequest(Today, []);
            var body = Body(request);

            var server = Assert.Single(body.GetProperty("mcp_servers").EnumerateArray().ToList());
            Assert.Equal("url", server.GetProperty("type").GetString());
            Assert.Equal("tw-stock", server.GetProperty("name").GetString());
            Assert.Equal("https://TW-Stock-MCP-Server.fastmcp.app/mcp", server.GetProperty("url").GetString());
            Assert.False(server.TryGetProperty("authorization_token", out _));

            Assert.Equal(2, body.GetProperty("tools").GetArrayLength());
            Assert.Equal(
                new[]
                {
                    "get_realtime_quote", "get_stock_history", "get_technical_indicators",
                    "get_twse_institutional_investors_summary", "get_company_monthly_revenue",
                },
                AnthropicChatService.AlwaysLoadedTools);

            // The MCP connector's beta goes with the request (as a header, outside the body).
            Assert.Contains("mcp-client-2025-11-20", JsonSerializer.Serialize(request));
        }

        [Fact]
        public void TheBodyHasNoFieldBeyondTheOnesPinnedHere()
        {
            var body = Body(AnthropicChatService.BuildRequest(Today, [], "# 市場背景"));

            Assert.Equal(
                new[] { "cache_control", "max_tokens", "mcp_servers", "messages", "model", "output_config", "system", "tools" },
                body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        }

        /// <summary>The request body as it goes over the wire.</summary>
        private static JsonElement Body(Anthropic.Models.Beta.Messages.MessageCreateParams request) =>
            JsonSerializer.SerializeToElement(request).GetProperty("RawBodyData");
    }
}
