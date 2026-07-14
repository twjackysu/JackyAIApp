namespace JackyAIApp.Server.Common
{
    /// <summary>
    /// Fixed, controlled vocabulary for the Portfolio net-worth-history "view by 產業" grouping.
    /// Deliberately NOT open-ended — this drives a stacked-area chart, so the set of buckets must
    /// stay stable across requests/users (drift would fragment the same symbol into different
    /// series over time, or endlessly grow the chart's legend/color palette). AI classification
    /// (see StockCategoryService) must map every symbol into exactly one of these; anything the
    /// model returns that isn't one of these names is coerced to <see cref="Other"/> rather than
    /// trusted verbatim. See .claude/plans/portfolio-industry-classification.md for the full design.
    ///
    /// Deliberately excludes region/currency (that's a separate, orthogonal viewBy axis) — 市值型
    /// is one bucket regardless of whether it's a TW or US broad-market ETF.
    /// </summary>
    public enum PortfolioCategory
    {
        // Individual-stock industries — also where sector/thematic ETFs land (e.g. SOXX → Semiconductor).
        Semiconductor,      // 半導體
        TechSoftware,       // 科技軟體 (software/電子零組件/網通/AI硬體)
        Financials,         // 金融
        Manufacturing,      // 傳產製造 (鋼鐵/塑化/機械/汽車零組件/紡織)
        Consumer,           // 民生消費 (食品/零售/餐飲/觀光)
        Healthcare,         // 醫療生技
        Materials,          // 能源原物料 (石油/綠能/原物料/商品)
        Utilities,          // 電信公用 (電信/水電燃氣)
        RealEstate,         // 房地產REITs (營建 + 個別REIT股 + REIT ETF, merged — same exposure)

        // ETFs/funds that can't be attributed to a single industry.
        BroadMarket,        // 市值型 — NOT split by region; leveraged/inverse ETFs classify by underlying here
        DividendEquity,     // 高股息
        Bond,               // 債券 (bond ETFs + bond mutual funds)

        // Non-classified assets — mapped directly, never sent to the AI.
        Cash,               // 現金
        Liability,          // 負債

        // AI-uncertain / genuinely unclassifiable (commodities, crypto, etc.) fallback.
        Other,              // 其他
    }

    /// <summary>Grouping dimension for GetNetWorthHistoryAsync's "view by" chart toggle.</summary>
    public enum NetWorthGroupBy
    {
        /// <summary>Current/default behavior: TwStock/UsStock/Cash/Fund/Liability.</summary>
        AssetType,
        /// <summary>PortfolioCategory buckets (AI-classified, DB-cached per symbol).</summary>
        Category,
        /// <summary>Position's declared currency: "TWD" | "USD".</summary>
        Currency,
    }
}
