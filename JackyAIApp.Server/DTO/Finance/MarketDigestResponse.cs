namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// "今日市場AI摘要" — a unified, plain-language daily digest synthesised from the day's
    /// market data, global financial news, and Taiwan major-announcement filings. Replaces the
    /// old per-stock card view with a layered narrative (global macro + Taiwan focus).
    /// </summary>
    public class MarketDigestResponse
    {
        public List<DigestPoint> Points { get; set; } = new();

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class DigestPoint
    {
        /// <summary>"global" (全球總經) | "taiwan" (台股焦點).</summary>
        public string Category { get; set; } = "global";

        /// <summary>One-line headline (Traditional Chinese).</summary>
        public string Headline { get; set; } = string.Empty;

        /// <summary>One-line plain explanation / cause (Traditional Chinese).</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>"bullish" | "bearish" | "neutral".</summary>
        public string Sentiment { get; set; } = "neutral";

        /// <summary>Optional TW stock code when the point is about a specific company.</summary>
        public string? StockCode { get; set; }
    }
}
