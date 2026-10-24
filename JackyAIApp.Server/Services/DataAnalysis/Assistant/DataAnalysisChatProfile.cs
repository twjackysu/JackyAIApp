using JackyAIApp.Server.Services.Assistant;

namespace JackyAIApp.Server.Services.DataAnalysis.Assistant
{
    /// <summary>
    /// The /data-analysis page's assistant: a data analyst for the source on the reader's screen
    /// (from the page context, <see cref="DataAnalysisPage"/>), which it reads through
    /// <see cref="AskDataTool"/> (questions) and <see cref="DescribeDataTool"/> (its columns). No
    /// tw-stock, no background.
    /// </summary>
    public class DataAnalysisChatProfile(
        INlSummarizeService nlSummarize, IDataAnalysisQueryService queries, ISessionDataStore sessions,
        IUsageLimitService usageLimits) : IChatProfile
    {
        public const string ProfileKey = "data-analysis";

        public string Key => ProfileKey;

        public string UsageLogFeature => "chatbot.data_analysis";

        public Task<ChatProfileSetup> SetupAsync(ChatProfileContext context, CancellationToken ct)
        {
            var page = DataAnalysisPage.From(context);
            return Task.FromResult(new ChatProfileSetup(
                SystemPromptFor(context.TaipeiToday), null, AttachTwStock: false,
                Tools: [new AskDataTool(nlSummarize, usageLimits, page), new DescribeDataTool(queries, sessions, page)]));
        }

        /// <summary>
        /// Today's date goes in up front, for questions like 「上個月」 or 「今年」; the tool's own pipeline
        /// works its relative dates out from the data's latest date.
        /// </summary>
        internal static string SystemPromptFor(DateOnly taipeiToday) =>
            $"今天是 {taipeiToday:yyyy-MM-dd}（台北時間）。\n" +
            "你是 JackyAIApp「資料分析」頁面上的資料分析助手，幫使用者理解他畫面上選定的那份資料。\n" +
            "- 不確定有哪些欄位、或使用者問資料裡有什麼時，先用 describe_data 看欄位與日期範圍。\n" +
            "- 需要數字時一律用 ask_data 查詢，不要自己編造或估算；一次問一件事，需要比較、拆解或追問原因時分幾次查。\n" +
            "- 查到的結果會在對話中畫成圖表（ask_data 的 chart 參數選圖），回答時不用再把整張表列出來，改成重點：最大最小、趨勢、異常、占比，並提出值得再看的方向。\n" +
            "- ask_data 回傳的 notes 或 unmatched 表示有條件沒套用（資料裡沒有那個值），一定要告訴使用者。\n" +
            "- 查詢失敗或沒有資料時照實說明，並建議使用者怎麼調整問題。\n" +
            "只回答和這份資料、資料分析或本網站功能相關的問題，其他問題禮貌婉拒。用繁體中文，簡潔明確地回答；回覆中的所有文字，包括呼叫工具之間的說明，都用繁體中文。";
    }
}
