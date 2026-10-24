namespace JackyAIApp.Server.Services.Assistant.Studio
{
    /// <summary>One page of the site the assistant can send the reader to.</summary>
    /// <param name="Key">What the model names it by.</param>
    /// <param name="Path">Its address on the site.</param>
    /// <param name="Title">What the reader calls it.</param>
    /// <param name="Description">What it is for, for the model.</param>
    public sealed record SitePage(string Key, string Path, string Title, string Description);

    /// <summary>
    /// The site's pages, as the client's app registry (<c>jackyaiapp.client/src/constants/apps.tsx</c>) and
    /// the English app's routes have them.
    /// </summary>
    public static class SitePages
    {
        public static readonly IReadOnlyList<SitePage> All =
        [
            new("home", "/", "首頁", "所有 app 的入口"),
            new("portfolio", "/portfolio", "投資組合", "記錄持股（台股、美股、基金、現金、負債、不動產）、報酬率、XIRR、回撤與大盤比較"),
            new("finance", "/finance", "Stock Intelligence 台股分析", "台股個股分析、綜合量化分析、AI 趨勢分析、今日市場 AI 摘要，以及會查即時台股資料的 AI 助理"),
            new("english", "/english", "英文學習", "英文學習 app 的首頁"),
            new("dictionary", "/english/dictionary", "字典", "查英文單字：音標、發音、中英釋義、例句、同反義字"),
            new("repository", "/english/repository", "我的單字庫", "使用者收藏的單字"),
            new("exam", "/english/exam", "測驗", "克漏字、翻譯、情境對話、造句、聽力、閱讀、寫作、聽寫"),
            new("daily", "/english/daily", "每日挑戰", "每天 5 題的單字小測驗"),
            new("review", "/english/review", "複習", "間隔重複（SM-2）的單字卡複習"),
            new("stats", "/english/stats", "學習統計", "連續天數、XP、等級、週報、排行榜"),
            new("jobs", "/jobs", "找工作", "104 與 Cake 的職缺彙整"),
            new("data-analysis", "/data-analysis", "資料分析", "查詢與彙總表格資料（SQL Server、Azure Data Explorer、上傳檔案），有自己的資料分析 AI 助理"),
            new("dashboards", "/dashboards", "Dashboards", "由資料分析結果做成的儀表板"),
            new("connectors", "/connectors", "Connectors", "管理 AI 可以使用的工具：本站功能與使用者自己的 MCP server，以及每個工具的權限"),
            new("pdf", "/pdf", "PDF 解鎖", "移除 PDF 密碼"),
            new("slideshow", "/slideshow", "資料夾幻燈片", "播放使用者選的資料夾裡的照片（只在瀏覽器裡執行）"),
        ];

        public static SitePage? Find(string? key) => All.FirstOrDefault(p => p.Key == key);
    }
}
