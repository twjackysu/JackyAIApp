namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// "今日市場重點" — a few plain-language takeaways synthesised from the day's market data,
    /// so the user can grasp what matters in a couple of minutes.
    /// </summary>
    public class MarketBriefResponse
    {
        public List<BriefPoint> Points { get; set; } = new();

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class BriefPoint
    {
        /// <summary>One-line headline (Traditional Chinese).</summary>
        public string Headline { get; set; } = string.Empty;

        /// <summary>One-line plain explanation (Traditional Chinese).</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>"bullish" | "bearish" | "neutral".</summary>
        public string Sentiment { get; set; } = "neutral";
    }
}
