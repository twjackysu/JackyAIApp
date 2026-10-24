using JackyAIApp.Server.Services.Assistant;

namespace JackyAIApp.Server.Services.Finance.Assistant
{
    /// <summary>
    /// The /finance page's assistant (Stock Intelligence): a Taiwan-market assistant with the
    /// tw-stock MCP server attached and the page's market data as its background.
    /// </summary>
    public class FinanceChatProfile(IMarketBackground marketBackground, ILogger<FinanceChatProfile> logger) : IChatProfile
    {
        public const string ProfileKey = "finance";

        public string Key => ProfileKey;

        public string UsageLogFeature => "chatbot.tw_stock";

        public async Task<ChatProfileSetup> SetupAsync(ChatProfileContext context, CancellationToken ct) =>
            SetupFor(context.TaipeiToday, await MarketBackgroundAsync(ct));

        /// <summary>Finance's setup for a day, with that background (null for none).</summary>
        internal static ChatProfileSetup SetupFor(DateOnly taipeiToday, string? marketBackground) =>
            new(SystemPromptFor(taipeiToday), marketBackground, AttachTwStock: true);

        /// <summary>The page's market data as background; the turn goes ahead without it if it cannot be had.</summary>
        private async Task<string?> MarketBackgroundAsync(CancellationToken ct)
        {
            try
            {
                return await marketBackground.GetAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Chat market background unavailable; answering without it");
                return null;
            }
        }

        /// <summary>
        /// Today's date goes in up front: the model's training data ends well before it, and
        /// without it a question about "today" or "the latest" is answered from that cutoff rather
        /// than looked up with the tool.
        /// </summary>
        /// <remarks>
        /// The scope below follows what the tw-stock server can look up. It mostly decides which
        /// questions are in scope — the tools' own descriptions say how to use them.
        /// </remarks>
        internal static string SystemPromptFor(DateOnly taipeiToday) =>
            $"今天是 {taipeiToday:yyyy-MM-dd}（台北時間）。\n" +
            "你是JackyAIApp網站上的台股投資助手，用tw-stock工具查詢台灣市場的資料再回答。可查的範圍：\n" +
            "- 個股（上市、上櫃、興櫃）：股價與技術面、財報（損益、資產負債、現金流量，可回溯多期）、月營收、股利與除權息、法說會、重大訊息、董監與內部人持股、庫藏股\n" +
            "- 籌碼：三大法人與外資動向、融資融券、集保股權分散（大戶與散戶）、當沖、處置與注意股\n" +
            "- ETF：基本資料、折溢價、規模與受益人數、申購買回、成分股\n" +
            "- 期貨與選擇權：行情（可回溯，含盤中走勢）、三大法人與大額交易人部位、Put/Call Ratio\n" +
            "- 總體經濟：景氣燈號與領先、同時、落後指標、PMI／NMI、匯率\n" +
            "回答原則：數字以工具查到的為準，並註明資料日期；查無資料或不確定時誠實說明，不要編造。" +
            "使用者附上的檔案（報告、截圖、表格）可以一起分析。給出買賣方向的看法時，註明僅供參考、不構成投資建議。" +
            "只回答台灣金融市場、投資理財或本網站功能相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答。\n" +
            // Unsure whether a tool it found can be called again, the model searched aimlessly
            // and once ended the turn with no answer.
            "工具採延遲載入：其他工具先用 tool_search_tool_regex 搜尋，pattern 可中英文關鍵字並用（例如「外資|institutional」）。" +
            "搜到的工具在整段對話中都能直接重複呼叫，不必再搜尋。\n" +
            // Every text delta reaches the reader, the notes between tool calls included; on a
            // retried tool call Haiku wrote those in English.
            "回覆中的所有文字，包括呼叫工具之間的說明，都用繁體中文。";
    }
}
