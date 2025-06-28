namespace JackyAIApp.Server.DTO
{
    public class StockTrendAnalysis
    {
        public string StockCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public decimal? CurrentPrice { get; set; }
        public string ShortTermTrend { get; set; } = string.Empty; // bullish, bearish, neutral
        public string MediumTermTrend { get; set; } = string.Empty;
        public string LongTermTrend { get; set; } = string.Empty;
        public string ShortTermSummary { get; set; } = string.Empty;
        public string MediumTermSummary { get; set; } = string.Empty;
        public string LongTermSummary { get; set; } = string.Empty;
        public List<string> KeyFactors { get; set; } = new();
        public List<string> RiskFactors { get; set; } = new();
        public string Recommendation { get; set; } = string.Empty; // buy, sell, hold
        public string ConfidenceLevel { get; set; } = string.Empty; // high, medium, low
        public DateTime LastUpdated { get; set; }
        public string DataSource { get; set; } = string.Empty;
    }

    public class StockSearchRequest
    {
        public string StockCodeOrName { get; set; } = string.Empty;
    }
}