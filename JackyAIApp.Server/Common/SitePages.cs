namespace JackyAIApp.Server.Common
{
    /// <summary>Title and description for a page's server-rendered metadata.</summary>
    internal sealed record PageMetadata(string Title, string Description);

    /// <summary>
    /// Which metadata each part of the site gets in its served HTML — the text a link preview
    /// (LINE, Teams, Facebook, Slack...) shows, since those crawlers read the raw HTML and never run
    /// the SPA's JavaScript. Names and descriptions mirror the apps in
    /// jackyaiapp.client/src/constants/apps.tsx; keep the two in step.
    /// </summary>
    internal static class SitePages
    {
        internal const string SiteName = "Jacky Studio";

        internal static readonly PageMetadata Home = new(
            "Jacky Studio — AI 學習、投資與實用工具",
            "AI 英文學習、台股分析與投資組合追蹤，以及 PDF 解鎖、職缺搜尋等實用工具。");

        private static readonly (string PathPrefix, string Name, string Description)[] Apps =
        [
            ("/portfolio", "Wealth Portfolio", "投資組合與資產追蹤"),
            ("/finance", "Stock Intelligence", "AI 股票分析與市場情報"),
            ("/english", "English Coach", "AI 英文學習與單字訓練"),
            ("/pdf", "PDF Unlocker & Compressor", "解除 PDF 密碼保護並壓縮檔案大小。"),
            ("/slideshow", "Folder Slideshow", "選一個資料夾，播放裡面的照片。"),
            ("/jobs", "Job Finder", "職缺搜尋與工作媒合"),
            ("/data-analysis", "Data Analysis", "AI 資料分析與自然語言查詢"),
            ("/dashboards", "Data Dashboard", "資料視覺化與互動儀表板"),
            ("/connectors", "Connected Services", "管理已連接的外部資料來源與服務。"),
        ];

        /// <summary>The app whose path the request is under (whole path segments only, so
        /// /portfolio-x is not /portfolio), or the site-wide home metadata for anything else.</summary>
        internal static PageMetadata ForPath(PathString path)
        {
            foreach (var (prefix, name, description) in Apps)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                    return new PageMetadata($"{name} | {SiteName}", description);
            }
            return Home;
        }
    }
}
