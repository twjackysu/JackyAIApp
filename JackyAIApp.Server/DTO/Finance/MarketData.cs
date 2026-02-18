namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Unified market data container used across all analysis layers.
    /// </summary>
    public class MarketData
    {
        /// <summary>Stock code, e.g., "2330"</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Company name, e.g., "台積電"</summary>
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>Historical daily OHLCV data</summary>
        public List<DailyPrice> HistoricalPrices { get; set; } = new();

        /// <summary>Fundamental data points</summary>
        public FundamentalData? Fundamentals { get; set; }

        /// <summary>Chip (籌碼) analysis data</summary>
        public ChipData? Chips { get; set; }

        /// <summary>When this data was fetched</summary>
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Daily OHLCV price record.
    /// </summary>
    public class DailyPrice
    {
        public DateTime Date { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
        public long Turnover { get; set; }
        public int Transactions { get; set; }
    }

    /// <summary>
    /// Fundamental data from TWSE APIs.
    /// </summary>
    public class FundamentalData
    {
        /// <summary>本益比 (P/E Ratio)</summary>
        public decimal? PERatio { get; set; }

        /// <summary>股價淨值比 (P/B Ratio)</summary>
        public decimal? PBRatio { get; set; }

        /// <summary>殖利率 (Dividend Yield %)</summary>
        public decimal? DividendYield { get; set; }

        /// <summary>月營收 (千元)</summary>
        public decimal? MonthlyRevenue { get; set; }

        /// <summary>月營收年增率 (%)</summary>
        public decimal? RevenueYoY { get; set; }

        /// <summary>月營收月增率 (%)</summary>
        public decimal? RevenueMoM { get; set; }
    }

    /// <summary>
    /// Chip (籌碼面) data from TWSE APIs.
    /// </summary>
    public class ChipData
    {
        /// <summary>融資今日餘額 (張)</summary>
        public long? MarginBalance { get; set; }

        /// <summary>融資昨日餘額 (張)</summary>
        public long? MarginPreviousBalance { get; set; }

        /// <summary>融資限額 (張)</summary>
        public long? MarginLimit { get; set; }

        /// <summary>融券今日餘額 (張)</summary>
        public long? ShortBalance { get; set; }

        /// <summary>融券昨日餘額 (張)</summary>
        public long? ShortPreviousBalance { get; set; }

        /// <summary>融券限額 (張)</summary>
        public long? ShortLimit { get; set; }

        /// <summary>資券互抵 (張)</summary>
        public long? OffsetVolume { get; set; }

        /// <summary>外資持股比率 (%)</summary>
        public decimal? ForeignHoldingPercentage { get; set; }

        /// <summary>借券賣出可用餘額</summary>
        public long? SBLAvailableVolume { get; set; }
    }
}
