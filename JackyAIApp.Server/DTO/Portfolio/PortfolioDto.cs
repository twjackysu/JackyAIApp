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
        // Total amount owed, positive. Already subtracted out of TotalNetWorthTWD.
        public decimal LiabilityTWD { get; set; }
        // P&L only over positions that have a cost basis
        public decimal CostBasisTWD { get; set; }
        public decimal UnrealizedPnlTWD { get; set; }
        public decimal UnrealizedPnlPct { get; set; }
        // Money-weighted annualized return (XIRR) over all snapshots; null when not computable.
        public decimal? AnnualizedReturnPct { get; set; }
        // Change vs the previous trading day's close, holding today's exact holdings constant
        // (isolates price movement). Null when no prior-day price is available for every holding.
        public decimal? DayChangeTWD { get; set; }
        public decimal? DayChangePct { get; set; }
        // Net market exposure: Σ market value × exposure factor (leveraged ETFs amplify, bonds count as 0).
        public decimal MarketExposureTWD { get; set; }
        public decimal ExposureRatioPct { get; set; } // exposure ÷ net worth × 100 (can exceed 100 with leverage)
        public List<PositionDto> Positions { get; set; } = [];
    }

    public class NetWorthDataPoint
    {
        public string Date { get; set; } = "";
        public decimal TotalTWD { get; set; }
        public decimal TwStockTWD { get; set; }
        public decimal UsStockTWD { get; set; }
        public decimal CashTWD { get; set; }
        public decimal FundTWD { get; set; }
        // Total amount owed, positive. Already subtracted out of TotalTWD.
        public decimal LiabilityTWD { get; set; }
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
}
