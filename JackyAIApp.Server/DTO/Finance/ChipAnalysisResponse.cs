namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Response model for the chip analysis endpoint.
    /// </summary>
    public class ChipAnalysisResponse
    {
        /// <summary>Stock code</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Company name</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Raw chip data from TWSE APIs</summary>
        public ChipData ChipData { get; set; } = new();

        /// <summary>Calculated chip indicators with scores and signals</summary>
        public List<IndicatorResult> Indicators { get; set; } = new();

        /// <summary>When this analysis was generated</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
