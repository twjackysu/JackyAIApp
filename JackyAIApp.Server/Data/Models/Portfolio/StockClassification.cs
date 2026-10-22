namespace JackyAIApp.Server.Data.Models.Portfolio
{
    /// <summary>
    /// The AI classification of a held symbol (TW or US stock, or fund), for the net-worth-history
    /// "view by 產業" chart and the holdings' tags. Keyed by Symbol alone: TW codes are numeric, US
    /// tickers alphabetic and FundRich fund IDs carry an alphabetic prefix (e.g. "ALI006"), so they
    /// never collide. Classified lazily by StockCategoryService, once per symbol; CASH and LIABILITY
    /// never get a row (they map to a fixed category).
    /// </summary>
    public class StockClassification
    {
        public required string Symbol { get; set; }

        /// <summary>Fixed-vocabulary bucket for the chart — PortfolioCategory enum stored as its string
        /// name.</summary>
        public required string Category { get; set; }

        /// <summary>Free-form Traditional-Chinese label from the AI classifier (e.g. "晶圓代工",
        /// "高股息台股ETF") — display-only (tooltip/detail table), never used for chart grouping,
        /// so inconsistency across symbols here doesn't matter.</summary>
        public string? SubCategory { get; set; }

        /// <summary>2-4 free-form Traditional-Chinese theme tags from the AI classifier, stored
        /// comma-separated (e.g. "雲端,AI,廣告"). Unlike <see cref="SubCategory"/> (exactly one,
        /// mutually exclusive, safe to stack in a chart), a holding can carry several tags — AMZN
        /// is simultaneously 電商 and 雲端 — so tags must NEVER drive a stacked/summed chart
        /// (double counting); display and non-stacked views only. CSV rather than a join table on
        /// purpose: single-user app, always read whole-row into memory, normalization buys nothing.</summary>
        public string? Tags { get; set; }

        public DateTime ClassifiedAt { get; set; } = DateTime.UtcNow;
    }
}
