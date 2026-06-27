namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// At-a-glance market dashboard: key indices, FX, and other instruments the user checks daily.
    /// </summary>
    public class MarketOverviewResponse
    {
        public List<MarketQuote> Quotes { get; set; } = new();

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class MarketQuote
    {
        /// <summary>Source ticker symbol (e.g., "^GSPC", "TWD=X").</summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Display name (e.g., "S&amp;P 500").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Grouping key: "US" / "TW" / "FX" / "Other".</summary>
        public string Group { get; set; } = string.Empty;

        /// <summary>Latest price / index level / rate. Null when the source was unavailable.</summary>
        public decimal? Price { get; set; }

        /// <summary>Absolute change vs previous close.</summary>
        public decimal? Change { get; set; }

        /// <summary>Percentage change vs previous close.</summary>
        public decimal? ChangePercent { get; set; }
    }
}
