namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Key macro/market indicators (FRED) for the Indicator Explorer: what it is, the latest
    /// value, and recent history for a chart.
    /// </summary>
    public class IndicatorExplorerResponse
    {
        public List<IndicatorSeries> Indicators { get; set; } = new();

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class IndicatorSeries
    {
        /// <summary>Our stable id (e.g., "us10y").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name (Traditional Chinese).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Plain-language "what is it" (Traditional Chinese).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Value unit/suffix (e.g., "%"), may be empty.</summary>
        public string Unit { get; set; } = string.Empty;

        public decimal? LatestValue { get; set; }

        public string? LatestDate { get; set; }

        /// <summary>Previous observation, for an at-a-glance change.</summary>
        public decimal? PreviousValue { get; set; }

        /// <summary>Recent history (downsampled) for the chart, oldest first.</summary>
        public List<IndicatorPoint> Points { get; set; } = new();
    }

    public class IndicatorPoint
    {
        public string Date { get; set; } = string.Empty;

        public decimal Value { get; set; }
    }

    /// <summary>AI reading for a single indicator.</summary>
    public class IndicatorInsight
    {
        public string Insight { get; set; } = string.Empty;
    }
}
