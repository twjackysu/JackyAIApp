namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Response model for the technical analysis endpoint.
    /// </summary>
    public class TechnicalAnalysisResponse
    {
        /// <summary>Stock code</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Company name</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Latest closing price</summary>
        public decimal? LatestClose { get; set; }

        /// <summary>Number of historical data points used</summary>
        public int DataPointCount { get; set; }

        /// <summary>Date range of data</summary>
        public string DataRange { get; set; } = string.Empty;

        /// <summary>All calculated indicators</summary>
        public List<IndicatorResult> Indicators { get; set; } = new();

        /// <summary>When this analysis was generated</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
