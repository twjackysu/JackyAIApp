namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Macro economy overview data for the Stock Intelligence / Market Analysis.
    /// </summary>
    public class MacroEconomyResponse
    {
        /// <summary>TAIEX market index data (recent days)</summary>
        public List<MarketIndexDay> MarketIndex { get; set; } = new();

        /// <summary>Key sector indices</summary>
        public List<SectorIndex> SectorIndices { get; set; } = new();

        /// <summary>Market-wide margin trading summary</summary>
        public MarginSummary? Margin { get; set; }

        /// <summary>Key exchange rates</summary>
        public List<ExchangeRate> ExchangeRates { get; set; } = new();

        /// <summary>Bank interest rates (latest)</summary>
        public BankRate? BankRate { get; set; }

        /// <summary>Data generation timestamp</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class MarketIndexDay
    {
        /// <summary>Date string (民國 format e.g., "1150211")</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>TAIEX closing index</summary>
        public decimal TAIEX { get; set; }

        /// <summary>Daily change</summary>
        public decimal Change { get; set; }

        /// <summary>Trade volume (shares)</summary>
        public long TradeVolume { get; set; }

        /// <summary>Trade value (NTD)</summary>
        public long TradeValue { get; set; }

        /// <summary>Number of transactions</summary>
        public long Transaction { get; set; }
    }

    public class SectorIndex
    {
        /// <summary>Date string (民國 format e.g., "1150211") — TWSE's MI_INDEX endpoint only
        /// publishes the most recently finalized trading day, which can lag "today" by a day or
        /// more (e.g. before today's session has closed, or over a weekend), with nothing in the
        /// response itself flagging that lag. Surfaced so the UI can show which day this actually
        /// is instead of silently implying it's live.</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>Sector name</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Closing index value</summary>
        public decimal CloseIndex { get; set; }

        /// <summary>Change direction (+/-)</summary>
        public string Direction { get; set; } = string.Empty;

        /// <summary>Change points</summary>
        public decimal ChangePoints { get; set; }

        /// <summary>Change percentage</summary>
        public decimal ChangePercent { get; set; }
    }

    public class MarginSummary
    {
        /// <summary>Total margin buy volume (張)</summary>
        public long MarginBuyTotal { get; set; }

        /// <summary>Total margin sell volume (張)</summary>
        public long MarginSellTotal { get; set; }

        /// <summary>Total margin balance (張)</summary>
        public long MarginBalanceTotal { get; set; }

        /// <summary>Total margin balance at the previous trading day's close (張)</summary>
        public long MarginPrevBalanceTotal { get; set; }

        /// <summary>Total short sell volume (張)</summary>
        public long ShortSellTotal { get; set; }

        /// <summary>Total short buy volume (張)</summary>
        public long ShortBuyTotal { get; set; }

        /// <summary>Total short balance (張)</summary>
        public long ShortBalanceTotal { get; set; }

        /// <summary>Total short balance at the previous trading day's close (張)</summary>
        public long ShortPrevBalanceTotal { get; set; }

        /// <summary>
        /// Day's change in the margin balance (張): today's balance minus the previous day's. Not
        /// buy − sell, which leaves out 融資現金償還 (repaid in cash). Null when no previous
        /// balance was reported, rather than passing today's whole balance off as the change.
        /// </summary>
        public long? MarginChange => MarginPrevBalanceTotal > 0 ? MarginBalanceTotal - MarginPrevBalanceTotal : null;

        /// <summary>
        /// Day's change in the short balance (張): today's balance minus the previous day's. Not
        /// sell − buy, which leaves out 融券現券償還 (returned in shares). Null as for
        /// <see cref="MarginChange"/>.
        /// </summary>
        public long? ShortChange => ShortPrevBalanceTotal > 0 ? ShortBalanceTotal - ShortPrevBalanceTotal : null;
    }

    public class ExchangeRate
    {
        /// <summary>Currency code (e.g., USD, JPY)</summary>
        public string Currency { get; set; } = string.Empty;

        /// <summary>Currency display name</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Bank buy (spot)</summary>
        public decimal? BuyRate { get; set; }

        /// <summary>Bank sell (spot)</summary>
        public decimal? SellRate { get; set; }
    }

    public class BankRate
    {
        /// <summary>Bank name</summary>
        public string BankName { get; set; } = string.Empty;

        /// <summary>Data period (民國 YYYMM)</summary>
        public string Period { get; set; } = string.Empty;

        /// <summary>1-year fixed deposit rate</summary>
        public decimal? OneYearFixed { get; set; }

        /// <summary>1-year floating deposit rate</summary>
        public decimal? OneYearFloating { get; set; }

        /// <summary>Base lending rate</summary>
        public decimal? BaseLendingRate { get; set; }
    }
}
