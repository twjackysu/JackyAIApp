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

        /// <summary>Set once a deep (~6-month) fetch has ever completed for this symbol — the
        /// explicit signal PersistentTwHistoricalProvider uses to decide whether a deep FetchAsync
        /// call needs to backfill further than whatever's already persisted. Deliberately NOT
        /// inferred from the oldest persisted row's date: a symbol with genuinely less than 6
        /// months of trading history (e.g. a recent IPO) would otherwise look "not deep enough"
        /// forever and get needlessly re-fetched on every single call.</summary>
        public DateTime? DeepFetchedAt { get; set; }

        /// <summary>Fixed-vocabulary bucket for the net-worth-history "view by 產業" chart —
        /// PortfolioCategory enum stored as its string name. Null = not yet classified (triggers
        /// lazy AI classification in StockCategoryService). CASH/LIABILITY never get a row here —
        /// they're mapped to Category.Cash/Liability directly without persistence.</summary>
        public string? Category { get; set; }

        /// <summary>Free-form Traditional-Chinese label from the AI classifier (e.g. "晶圓代工",
        /// "高股息台股ETF") — display-only (tooltip/detail table), never used for chart grouping,
        /// so inconsistency across symbols here doesn't matter.</summary>
        public string? SubCategory { get; set; }

        /// <summary>2-4 free-form Traditional-Chinese theme tags from the AI classifier, stored
        /// comma-separated (e.g. "雲端,AI,廣告"). Unlike <see cref="SubCategory"/> (exactly one,
        /// mutually exclusive, safe to stack in a chart), a holding can carry several tags — AMZN
        /// is simultaneously 電商 and 雲端 — so tags must NEVER drive a stacked/summed chart
        /// (double counting); display and non-stacked views only. CSV rather than a join table on
        /// purpose: single-user app, always read whole-row into memory, normalization buys nothing.</summary>
        public string? Tags { get; set; }

        public DateTime? ClassifiedAt { get; set; }
    }

    /// <summary>
    /// Full TW listed-company table (all TSE+OTC symbols), persisted so the holdings-editor's
    /// stock search never has to call TWSE's isin.twse.com.tw lookup synchronously on every
    /// keystroke — that endpoint is slow and has real outage/maintenance windows, and a failed
    /// call there used to surface as "查無股票" for every symbol, not just ones never seen before.
    /// Refreshed opportunistically (see PortfolioService.RefreshTwListedCompaniesIfStaleAsync):
    /// whole-table overwrite on a successful live fetch, since TWSE's own endpoint already returns
    /// the whole table per call — no per-row diffing. A failed refresh just serves the existing
    /// (slightly stale) rows rather than an empty result.
    /// </summary>
    public class TwListedCompany
    {
        [Key]
        public required string Symbol { get; set; }

        public required string Name { get; set; }

        public DateTime LastRefreshedAt { get; set; } = DateTime.UtcNow;
    }
}
