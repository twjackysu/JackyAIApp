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

        /// <summary>基本每股盈餘 (元)</summary>
        public decimal? EPS { get; set; }

        /// <summary>近四季累計 EPS (元)</summary>
        public decimal? TrailingEPS { get; set; }

        /// <summary>營業利益 (千元)</summary>
        public decimal? OperatingIncome { get; set; }

        /// <summary>稅後淨利 (千元)</summary>
        public decimal? NetIncome { get; set; }

        /// <summary>財報年度季別 (e.g., "2024Q3")</summary>
        public string? FiscalYearQuarter { get; set; }

        /// <summary>營收資料年月 (e.g., "11501")</summary>
        public string? RevenueMonth { get; set; }
    }

    /// <summary>
    /// Chip (籌碼面) data from TWSE APIs.
    /// </summary>
    public class ChipData
    {
        // === 融資 (Margin Trading) ===

        /// <summary>融資買進 (張)</summary>
        public long MarginBuyVolume { get; set; }

        /// <summary>融資賣出 (張)</summary>
        public long MarginSellVolume { get; set; }

        /// <summary>融資現金償還 (張)</summary>
        public long MarginCashRepayment { get; set; }

        /// <summary>融資前日餘額 (張)</summary>
        public long? MarginPreviousBalance { get; set; }

        /// <summary>融資今日餘額 (張)</summary>
        public long? MarginBalance { get; set; }

        /// <summary>融資限額 (張)</summary>
        public long? MarginLimit { get; set; }

        // === 融券 (Short Selling) ===

        /// <summary>融券買進 (張)</summary>
        public long ShortBuyVolume { get; set; }

        /// <summary>融券賣出 (張)</summary>
        public long ShortSellVolume { get; set; }

        /// <summary>融券現券償還 (張)</summary>
        public long ShortCashRepayment { get; set; }

        /// <summary>融券前日餘額 (張)</summary>
        public long? ShortPreviousBalance { get; set; }

        /// <summary>融券今日餘額 (張)</summary>
        public long? ShortBalance { get; set; }

        /// <summary>融券限額 (張)</summary>
        public long? ShortLimit { get; set; }

        /// <summary>資券互抵 (張)</summary>
        public long? OffsetVolume { get; set; }

        // === 外資 (Foreign Investors) ===

        /// <summary>外資持股比率 (%)</summary>
        public decimal? ForeignHoldingPercentage { get; set; }

        /// <summary>外資持股數</summary>
        public long? ForeignHoldingShares { get; set; }

        /// <summary>外資可投資股數</summary>
        public long? ForeignAvailableShares { get; set; }

        /// <summary>外資投資上限 (%)</summary>
        public decimal? ForeignUpperLimit { get; set; }

        // === 借券 (Securities Borrowing & Lending) ===

        /// <summary>借券賣出可用餘額</summary>
        public long? SBLAvailableVolume { get; set; }

        // === 董監事 (Director/Supervisor Holdings) ===

        /// <summary>董監事持股明細</summary>
        public List<DirectorHolding>? DirectorHoldings { get; set; }

        /// <summary>董監事持股合計</summary>
        public long TotalDirectorShares { get; set; }

        /// <summary>董監事設質合計</summary>
        public long TotalDirectorPledged { get; set; }

        /// <summary>董監事設質比率 (%)</summary>
        public decimal DirectorPledgeRatio { get; set; }

        // === 大股東 (Major Shareholders) ===

        /// <summary>持股逾10%大股東名單</summary>
        public List<string>? MajorShareholders { get; set; }

        // === 當沖 (Day Trading) ===

        /// <summary>是否暫停當沖</summary>
        public bool DayTradingSuspended { get; set; }
    }

    /// <summary>
    /// Director/Supervisor individual holding record.
    /// </summary>
    public class DirectorHolding
    {
        /// <summary>職稱 (e.g., 董事長本人, 董事之法人代表人)</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>姓名</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>目前持股</summary>
        public long CurrentShares { get; set; }

        /// <summary>設質股數</summary>
        public long PledgedShares { get; set; }

        /// <summary>設質比例</summary>
        public string PledgeRatio { get; set; } = string.Empty;
    }
}
