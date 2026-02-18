namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Comprehensive stock scoring response combining all indicator categories.
    /// </summary>
    public class StockScoreResponse
    {
        /// <summary>Stock code</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Company name</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Latest closing price</summary>
        public decimal? LatestClose { get; set; }

        /// <summary>Overall composite score (0-100)</summary>
        public decimal OverallScore { get; set; }

        /// <summary>Overall signal direction</summary>
        public SignalDirection OverallDirection { get; set; }

        /// <summary>Overall recommendation text</summary>
        public string Recommendation { get; set; } = string.Empty;

        /// <summary>Category-level score breakdown</summary>
        public List<CategoryScore> CategoryScores { get; set; } = new();

        /// <summary>All individual indicator results</summary>
        public List<IndicatorResult> Indicators { get; set; } = new();

        /// <summary>Risk assessment based on indicator divergence</summary>
        public RiskAssessment Risk { get; set; } = new();

        /// <summary>Data range used for analysis</summary>
        public string DataRange { get; set; } = string.Empty;

        /// <summary>When this scoring was generated</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Score breakdown for a single indicator category.
    /// </summary>
    public class CategoryScore
    {
        /// <summary>Category name (Technical, Chip, Fundamental)</summary>
        public IndicatorCategory Category { get; set; }

        /// <summary>Weighted average score for this category (0-100)</summary>
        public decimal Score { get; set; }

        /// <summary>Weight used in overall calculation (0-1)</summary>
        public decimal Weight { get; set; }

        /// <summary>Weighted contribution to overall score</summary>
        public decimal WeightedScore { get; set; }

        /// <summary>Signal direction for this category</summary>
        public SignalDirection Direction { get; set; }

        /// <summary>Summary of this category's signals</summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>Number of indicators in this category</summary>
        public int IndicatorCount { get; set; }
    }

    /// <summary>
    /// Risk assessment derived from indicator analysis.
    /// </summary>
    public class RiskAssessment
    {
        /// <summary>Risk level (Low, Medium, High, VeryHigh)</summary>
        public RiskLevel Level { get; set; }

        /// <summary>Risk factors identified</summary>
        public List<string> Factors { get; set; } = new();

        /// <summary>Indicator divergence score (higher = more conflicting signals)</summary>
        public decimal DivergenceScore { get; set; }
    }

    /// <summary>
    /// Risk level classification.
    /// </summary>
    public enum RiskLevel
    {
        Low,
        Medium,
        High,
        VeryHigh
    }
}
