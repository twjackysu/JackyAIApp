namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Result from the StockAnalysisBuilder — a flexible composite analysis.
    /// </summary>
    public class StockAnalysisResult
    {
        public string StockCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Market { get; set; } = "TW";
        public decimal? LatestClose { get; set; }
        public List<IndicatorResult> Indicators { get; set; } = new();
        public StockScoreResponse? Scoring { get; set; }
        public RiskAssessment? Risk { get; set; }
        public string DataRange { get; set; } = string.Empty;
        public AnalysisConfiguration Configuration { get; set; } = new();
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Captures what configuration the builder used.
    /// </summary>
    public class AnalysisConfiguration
    {
        public bool IncludeTechnical { get; set; } = true;
        public bool IncludeChip { get; set; } = true;
        public bool IncludeFundamental { get; set; } = true;
        public bool IncludeScoring { get; set; } = true;
        public bool IncludeRisk { get; set; } = true;
        public List<string> OnlyIndicators { get; set; } = new();
        public List<string> ExcludeIndicators { get; set; } = new();
    }

    /// <summary>
    /// Request model for the comprehensive analysis endpoint.
    /// </summary>
    public class StockAnalysisRequest
    {
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Market region: "TW" or "US". If null, auto-detected from stock code format.</summary>
        public string? Market { get; set; }

        public bool IncludeTechnical { get; set; } = true;
        public bool IncludeChip { get; set; } = true;
        public bool IncludeFundamental { get; set; } = true;
        public bool IncludeScoring { get; set; } = true;
        public bool IncludeRisk { get; set; } = true;
        public List<string>? OnlyIndicators { get; set; }
        public List<string>? ExcludeIndicators { get; set; }
        public decimal? TechnicalWeight { get; set; }
        public decimal? ChipWeight { get; set; }
        public decimal? FundamentalWeight { get; set; }
    }
}
