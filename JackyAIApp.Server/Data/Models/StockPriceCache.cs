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
    /// Persists which market (TSE/OTC) a TW stock code trades on, resolved once via TWSE's isin
    /// listed-company lookup (isin.twse.com.tw — slow, returns the whole table per call) and never
    /// re-resolved after that, since exchange listing is effectively permanent for a given code.
    /// </summary>
    public class StockSymbolInfo
    {
        [Key]
        public required string Symbol { get; set; }

        /// <summary>Currently always "TW" — Phase 1 scope.</summary>
        public required string Market { get; set; }

        /// <summary>"TSE" | "OTC" (TWStockLib.Models.MarketType, stored as string to avoid a hard
        /// dependency from the data model on the third-party package's enum).</summary>
        public required string TwMarketType { get; set; }

        public string? Name { get; set; }

        public DateTime LastResolvedAt { get; set; } = DateTime.UtcNow;
    }
}
