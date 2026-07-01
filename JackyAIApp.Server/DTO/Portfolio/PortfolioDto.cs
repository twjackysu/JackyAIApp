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
        public decimal MarketValueLocal { get; set; }
        public decimal MarketValueTWD { get; set; }
        public decimal? CostBasisTWD { get; set; }       // null when no cost entered
        public decimal? UnrealizedPnlTWD { get; set; }
        public decimal? UnrealizedPnlPct { get; set; }
        public bool PriceFetchFailed { get; set; }
    }

    public class PortfolioSummaryDto
    {
        public DateOnly? EffectiveDate { get; set; }
        public decimal UsdTwdRate { get; set; }
        public decimal TotalNetWorthTWD { get; set; }
        public decimal TwStockTWD { get; set; }
        public decimal UsStockTWD { get; set; }
        public decimal CashTWD { get; set; }
        public decimal FundTWD { get; set; }
        // P&L only over positions that have a cost basis
        public decimal CostBasisTWD { get; set; }
        public decimal UnrealizedPnlTWD { get; set; }
        public decimal UnrealizedPnlPct { get; set; }
        // Money-weighted annualized return (XIRR) over all snapshots; null when not computable.
        public decimal? AnnualizedReturnPct { get; set; }
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
