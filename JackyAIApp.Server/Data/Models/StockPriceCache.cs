using System.ComponentModel.DataAnnotations;

namespace JackyAIApp.Server.Data.Models.SQL
{
    /// <summary>
    /// One finalized daily OHLCV bar for a TW stock, shared across all users (a symbol's close on
    /// a given date is the same for everyone). Only ever holds bars for days that have already
    /// closed — today's still-moving intraday price must never be written here (see
    /// PersistentTwHistoricalProvider). Historical bars are immutable, so once a row exists it is
    /// never refetched.
    /// </summary>
    public class DailyStockPrice
    {
        [Key]
        public int Id { get; set; }

        public required string Symbol { get; set; }

        /// <summary>Currently always "TW" — Phase 1 scope. See portfolio-db-price-cache.md.</summary>
        public required string Market { get; set; }

        public required DateOnly TradeDate { get; set; }

        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>
    /// Per-symbol metadata cache, keyed by Symbol alone. Originally TW-only (market-type
    /// resolution), now also holds cross-market AI category classification (US stocks, funds).
    /// Symbol collisions across markets aren't a real concern in practice: TW codes are numeric
    /// (4-6 digits, optional trailing letter), US tickers are alphabetic, and FundRich fund IDs
    /// have an alphabetic prefix (e.g. "ALI006") — none of these formats overlap.
    /// </summary>
    public class StockSymbolInfo
    {
        [Key]
        public required string Symbol { get; set; }

        /// <summary>"TW" | "US" | "FUND".</summary>
        public required string Market { get; set; }

        /// <summary>"TSE" | "OTC" (TWStockLib.Models.MarketType, stored as string to avoid a hard
        /// dependency from the data model on the third-party package's enum). Only set for TW
        /// symbols — resolved via TWSE's isin listed-company lookup (isin.twse.com.tw — slow,
        /// returns the whole table per call) and never re-resolved after that, since exchange
        /// listing is effectively permanent for a given code. Null for US/FUND symbols.</summary>
        public string? TwMarketType { get; set; }

        public string? Name { get; set; }

        public DateTime LastResolvedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Fixed-vocabulary bucket for the net-worth-history "view by 產業" chart —
        /// PortfolioCategory enum stored as its string name. Null = not yet classified (triggers
        /// lazy AI classification in StockCategoryService). CASH/LIABILITY never get a row here —
        /// they're mapped to Category.Cash/Liability directly without persistence.</summary>
        public string? Category { get; set; }

        /// <summary>Free-form Traditional-Chinese label from the AI classifier (e.g. "晶圓代工",
        /// "高股息台股ETF") — display-only (tooltip/detail table), never used for chart grouping,
        /// so inconsistency across symbols here doesn't matter.</summary>
        public string? SubCategory { get; set; }

        public DateTime? ClassifiedAt { get; set; }
    }
}
