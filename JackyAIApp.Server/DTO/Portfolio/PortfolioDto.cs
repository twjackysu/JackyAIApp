using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.DTO.Portfolio
{
    // ── Save holdings (full snapshot replace) ────────────────────────────────

    public class SaveHoldingsRequest
    {
        /// <summary>Date these holdings take effect. Defaults to today if omitted.</summary>
        public DateOnly? EffectiveDate { get; set; }
        public List<PositionInput> Positions { get; set; } = [];
    }

    public class PositionInput
    {
        public required AssetType AssetType { get; set; }
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public string Market { get; set; } = "";
        public required string Currency { get; set; }
        public string? CostCurrency { get; set; } // currency the cost basis was paid in; defaults to Currency
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }   // fund NAV
        public decimal? CostBasis { get; set; }   // optional
        public string? ValuationMethod { get; set; } // REAL_ESTATE only: "市價" | "買價扣除折舊"
    }

    // ── Read models ──────────────────────────────────────────────────────────

    public class PositionDto
    {
        public AssetType AssetType { get; set; }
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public string Market { get; set; } = "";
        public string Currency { get; set; } = "";
        public string? CostCurrency { get; set; }
        public decimal Quantity { get; set; }
        public decimal CurrentPrice { get; set; }       // stock live / fund NAV / cash 1
        // Negative for LIABILITY (amount owed); positive for everything else.
        public decimal MarketValueLocal { get; set; }
        public decimal MarketValueTWD { get; set; }
        public decimal? CostBasisTWD { get; set; }       // null when no cost entered
        public decimal? UnrealizedPnlTWD { get; set; }
        public decimal? UnrealizedPnlPct { get; set; }
        public decimal ExposureFactor { get; set; } = 1m; // market exposure multiplier (2 for 正2, 0 for bond/cash)
        public bool PriceFetchFailed { get; set; }
        public string? ValuationMethod { get; set; } // REAL_ESTATE only: "市價" | "買價扣除折舊"
        // Cumulative pre-tax cash dividends earned while holding this position (0 = none/not applicable).
        public decimal DividendsTWD { get; set; }
        // Estimated brokerage fee + transaction tax if sold at the current price (TW STOCK only,
        // 0 for everything else). Already subtracted out of UnrealizedPnlTWD/Pct above.
        public decimal EstimatedSellCostTWD { get; set; }
    }

    public class PortfolioSummaryDto
    {
        public DateOnly? EffectiveDate { get; set; }
        public decimal UsdTwdRate { get; set; }
        // Net of liabilities: Σ positions.MarketValueTWD (LIABILITY contributes negatively). No special-casing.
        public decimal TotalNetWorthTWD { get; set; }
        public decimal TwStockTWD { get; set; }
        public decimal UsStockTWD { get; set; }
        public decimal CashTWD { get; set; }
        public decimal FundTWD { get; set; }
        // User-entered real-estate value (market estimate or cost-minus-depreciation, see
        // PositionDto.ValuationMethod). Zero market exposure (PortfolioExposure.Factor) but does
        // count toward TotalNetWorthTWD — deliberately, so a large home-equity draw against real
        // property doesn't make the exposure ratio look far more leveraged than it actually is.
        public decimal RealEstateTWD { get; set; }
        // Total amount owed, positive. Already subtracted out of TotalNetWorthTWD.
        public decimal LiabilityTWD { get; set; }
        // P&L only over positions that have a cost basis
        public decimal CostBasisTWD { get; set; }
        public decimal UnrealizedPnlTWD { get; set; }
        public decimal UnrealizedPnlPct { get; set; }
        // Σ Positions.EstimatedSellCostTWD — already netted into UnrealizedPnlTWD/TotalReturnPnlTWD/
        // AnnualizedReturnPct above, surfaced separately so the UI can show what was deducted.
        public decimal EstimatedSellCostTWD { get; set; }
        // Cumulative pre-tax cash dividends earned across the whole snapshot timeline (Σ Positions.DividendsTWD,
        // plus any from positions no longer held today).
        public decimal TotalDividendsTWD { get; set; }
        // Total (含息) return: UnrealizedPnlTWD + TotalDividendsTWD.
        public decimal TotalReturnPnlTWD { get; set; }
        public decimal TotalReturnPct { get; set; }
        // Money-weighted annualized return (XIRR) over all snapshots, including dividend inflows;
        // null when not computable.
        public decimal? AnnualizedReturnPct { get; set; }
        // Change vs the previous trading day's close, holding today's exact holdings constant
        // (isolates price movement). Null when no prior-day price is available for every holding.
        public decimal? DayChangeTWD { get; set; }
        public decimal? DayChangePct { get; set; }
        // Net market exposure: Σ market value × exposure factor (leveraged ETFs amplify, bonds count as 0).
        public decimal MarketExposureTWD { get; set; }
        public decimal ExposureRatioPct { get; set; } // exposure ÷ net worth (incl. real estate) × 100 (can exceed 100 with leverage)
        // Same numerator, denominator excludes RealEstateTWD — "how leveraged is just the liquid
        // portfolio", without a large real-estate value (which carries zero exposure itself, see
        // PortfolioExposure.Factor) diluting the ratio and making it look less leveraged than the
        // liquid holdings alone actually are.
        public decimal LiquidExposureRatioPct { get; set; }
        public List<PositionDto> Positions { get; set; } = [];
    }

    /// <summary>
    /// One day's net-worth breakdown, grouped by whatever dimension the request asked for
    /// (GetNetWorthHistoryAsync's groupBy: assetType/category/currency — see NetWorthGroupBy).
    /// <see cref="Groups"/>' key format depends on the mode:
    /// - assetType: "TwStock" | "UsStock" | "Cash" | "Fund" | "Property" | "Liability"
    /// - category: a PortfolioCategory enum member name (e.g. "Semiconductor")
    /// - currency: "TWD" | "USD"
    /// - holding: each position's own Name verbatim (e.g. "元大台灣50正2", "台積電", "現金")
    /// Group values are signed and sum to <see cref="TotalTWD"/> — Liability/debt is negative
    /// (unlike the old fixed LiabilityTWD field this replaced, which was a positive "amount owed").
    /// </summary>
    public class NetWorthDataPoint
    {
        public string Date { get; set; } = "";
        public decimal TotalTWD { get; set; }
        public List<NetWorthGroupValue> Groups { get; set; } = [];
    }

    public class NetWorthGroupValue
    {
        public string Key { get; set; } = "";
        public decimal ValueTWD { get; set; }
    }

    public class UpdateHistoryItem
    {
        public int SnapshotId { get; set; }
        public string EffectiveDate { get; set; } = "";
        public int PositionCount { get; set; }
        public decimal UsdTwdRate { get; set; }
    }

    // ── Stock lookup + price history ─────────────────────────────────────────

    public class StockLookupDto
    {
        public string Symbol { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public decimal CurrentPrice { get; set; }
        public string Currency { get; set; } = "";
    }

    public class PricePoint
    {
        public string Date { get; set; } = "";
        public decimal Close { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
    }

    // ── Fund search (FundRich) ───────────────────────────────────────────────

    public class FundSearchItem
    {
        public string FundId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsSell { get; set; }
    }

    // ── Stock search (TWStockLib list / Yahoo) ───────────────────────────────

    public class StockSearchItem
    {
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public string Market { get; set; } = "";
    }

    public class PortfolioAiSummaryDto
    {
        public string TrendLabel { get; set; } = "";
        public string Action { get; set; } = "";
        public string Signals { get; set; } = "";
    }

    public class HoldingPriceHistoryDto
    {
        public string Symbol { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Market { get; set; } = "";
        public string Currency { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal? AvgCost { get; set; }   // costBasis / quantity, null if no cost
        public List<PricePoint> Prices { get; set; } = [];
    }

    // ── AI industry/category classification ──────────────────────────────────

    /// <summary>Raw shape of the AI's classification response — see StockCategoryService.
    /// Category is validated against the PortfolioCategory enum before use (never trusted
    /// verbatim); SubCategory is free-form and display-only.</summary>
    public class CategoryClassificationResult
    {
        public required string Category { get; set; }
        public required string SubCategory { get; set; }
    }
}
