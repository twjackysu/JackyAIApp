namespace JackyAIApp.Server.DTO.Finance
{
    /// <summary>
    /// Insider trading transaction from SEC Form 4
    /// </summary>
    public class InsiderTransaction
    {
        /// <summary>Reporting owner name (e.g., "Tim Cook")</summary>
        public string OwnerName { get; set; } = string.Empty;

        /// <summary>Officer, Director, 10% Owner, Other</summary>
        public string Relationship { get; set; } = string.Empty;

        /// <summary>Transaction date (YYYY-MM-DD)</summary>
        public string TransactionDate { get; set; } = string.Empty;

        /// <summary>P (Purchase), S (Sale), A (Award/Grant), M (Exercise)</summary>
        public string TransactionCode { get; set; } = string.Empty;

        /// <summary>Number of shares</summary>
        public decimal Shares { get; set; }

        /// <summary>Price per share (null for grants)</summary>
        public decimal? PricePerShare { get; set; }

        /// <summary>Total value (Shares * PricePerShare)</summary>
        public decimal? TransactionValue { get; set; }

        /// <summary>Shares owned after transaction</summary>
        public decimal? SharesOwnedAfter { get; set; }

        /// <summary>SEC filing date</summary>
        public string FilingDate { get; set; } = string.Empty;

        /// <summary>SEC accession number (unique filing ID)</summary>
        public string AccessionNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated insider trading summary
    /// </summary>
    public class InsiderTradingSummary
    {
        /// <summary>Stock symbol</summary>
        public string StockCode { get; set; } = string.Empty;

        /// <summary>Recent transactions (last 90 days)</summary>
        public List<InsiderTransaction> RecentTransactions { get; set; } = new();

        /// <summary>Net insider buying (purchases - sales) in shares, last 90 days</summary>
        public decimal NetBuyingShares { get; set; }

        /// <summary>Net insider buying value, last 90 days</summary>
        public decimal? NetBuyingValue { get; set; }

        /// <summary>Number of insider purchases, last 90 days</summary>
        public int PurchaseCount { get; set; }

        /// <summary>Number of insider sales, last 90 days</summary>
        public int SaleCount { get; set; }

        /// <summary>Total purchase value, last 90 days</summary>
        public decimal? TotalPurchaseValue { get; set; }

        /// <summary>Total sale value, last 90 days</summary>
        public decimal? TotalSaleValue { get; set; }

        /// <summary>Data fetched at (UTC)</summary>
        public DateTime FetchedAt { get; set; }
    }
}
