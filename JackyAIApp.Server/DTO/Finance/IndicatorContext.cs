namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Context passed to each indicator calculator containing all available market data.
    /// </summary>
    public class IndicatorContext
    {
        /// <summary>Stock code</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Historical daily prices sorted by date ascending</summary>
        public List<DailyPrice> Prices { get; set; } = new();

        /// <summary>Fundamental data (may be null if not fetched)</summary>
        public FundamentalData? Fundamentals { get; set; }

        /// <summary>Chip data (may be null if not fetched)</summary>
        public ChipData? Chips { get; set; }

        /// <summary>
        /// Convenience: get the latest closing price.
        /// </summary>
        public decimal? LatestClose => Prices.Count > 0 ? Prices[^1].Close : null;

        /// <summary>
        /// Convenience: get closing prices as a decimal array (oldest first).
        /// </summary>
        public decimal[] ClosingPrices => Prices.Select(p => p.Close).ToArray();

        /// <summary>
        /// Convenience: get volumes as a long array (oldest first).
        /// </summary>
        public long[] Volumes => Prices.Select(p => p.Volume).ToArray();

        /// <summary>
        /// Convenience: get high prices as a decimal array (oldest first).
        /// </summary>
        public decimal[] HighPrices => Prices.Select(p => p.High).ToArray();

        /// <summary>
        /// Convenience: get low prices as a decimal array (oldest first).
        /// </summary>
        public decimal[] LowPrices => Prices.Select(p => p.Low).ToArray();
    }
}
