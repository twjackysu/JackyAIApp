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
    /// Per-symbol market-data metadata, keyed by Symbol alone: which TW exchange it trades on, and
    /// how far back its price history is stored. The AI classification Portfolio adds is in
    /// <see cref="Portfolio.StockClassification"/>, Portfolio's own table (until 2026-10 it was four
    /// columns here). Symbol collisions across markets aren't a real concern in practice: TW codes
    /// are numeric (4-6 digits, optional trailing letter), US tickers are alphabetic, and FundRich
    /// fund IDs have an alphabetic prefix (e.g. "ALI006") — none of these formats overlap.
    /// </summary>
    public class StockSymbolInfo
    {
        [Key]
        public required string Symbol { get; set; }

        /// <summary>"TW" | "US" | "FUND".</summary>
        public required string Market { get; set; }

        /// <summary>"TSE" | "OTC" (TWStockLib.MarketType, stored as string to avoid a hard
        /// dependency from the data model on the third-party package's enum). Only set for TW
        /// symbols — resolved from the TW listed-company list (TwListedCompany.Market) and never
        /// re-resolved after that, since exchange listing is effectively permanent for a given
        /// code. Null for US/FUND symbols.</summary>
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

        /// <summary>First day of the oldest month whose bars are fully persisted for this symbol,
        /// as far back as the background backfill has got (see TwHistoryBackfillService) — months
        /// from here to the present are all in <see cref="DailyStockPrice"/>. Null until a backfill
        /// has run; then the start is derived from <see cref="DeepFetchedAt"/> (the deep fetch's
        /// ~6-month window). Month granularity on purpose: the exchange is asked for whole months,
        /// so "covered" never has to mean "covered from the 14th".</summary>
        public DateOnly? HistoryCoveredFrom { get; set; }
    }

    /// <summary>
    /// TW listed symbols and names (上市 + 上櫃, warrants excluded), backing the holdings
    /// editor's stock search so a keystroke never waits on an exchange's servers. Filled by
    /// TwListedCompanyRefreshService from TWSE/TPEx OpenAPI, about twice a day: rows are upserted,
    /// and only removed after 30 days unseen, since a suspended stock drops out of the daily
    /// dumps (see TwListedCompanyService). A failed refresh leaves the rows as they were.
    /// </summary>
    public class TwListedCompany
    {
        [Key]
        public required string Symbol { get; set; }

        public required string Name { get; set; }

        /// <summary>"TSE" (上市) or "OTC" (上櫃), as TWStockLib's MarketType names them: which
        /// exchange's list the symbol came from. Null only on rows from before this column
        /// existed, until the next refresh fills it.</summary>
        [MaxLength(3)]
        public string? Market { get; set; }

        public DateTime LastRefreshedAt { get; set; } = DateTime.UtcNow;
    }
}
