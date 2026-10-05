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
        // Rate this position's foreign currency was converted to TWD at; null for TWD holdings.
        // Surfaced because it is not one number app-wide: funds use 集保's settlement rate (what
        // 基富通 shows) while everything else uses a mid-market rate, and the two differ enough
        // that a user reconciling against their broker needs to see which one produced this row.
        public decimal? FxRateUsed { get; set; }
        public string? FxRateSource { get; set; } // "TDCC" | "MID" | null
        public bool PriceFetchFailed { get; set; }
        public string? ValuationMethod { get; set; } // REAL_ESTATE only: "市價" | "買價扣除折舊"
        // Pre-tax cash dividends earned during this position's CURRENT unbroken holding period
        // (0 = none/not applicable). Scoped that way on purpose: sell a holding out entirely and
        // buy it back later and the earlier period's payouts belong to that closed period, not to
        // the position as it exists now — showing them here made a one-day-old holding look like it
        // had already paid. Earlier periods still count in PortfolioSummaryDto.TotalDividendsTWD.
        public decimal DividendsTWD { get; set; }
        // Estimated brokerage fee + transaction tax if sold at the current price (TW STOCK only,
        // 0 for everything else). Already subtracted out of UnrealizedPnlTWD/Pct above.
        public decimal EstimatedSellCostTWD { get; set; }
        // AI theme tags (0-4) from StockSymbolInfo.Tags — populated by a read-only lookup, so this
        // stays empty until the symbol has been classified (the 產業/子類別 chart tabs trigger
        // that lazily; the summary itself never calls the AI). Display-only: a holding can carry
        // several tags, so these must never be summed/stacked anywhere.
        public List<string> Tags { get; set; } = [];
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
        // TwStockTWD + UsStockTWD + CashTWD + FundTWD — excludes real estate, NOT net of
        // liability. Denominator for UnrealizedPnlPct/TotalReturnPct below (real estate is manually-valued
        // and shouldn't dilute a price-driven %, and netting out debt would inflate the ratio the way
        // "÷ (assets - debt)" does). Also surfaced directly so the UI can show it alongside the
        // net-of-liability "流動資產淨值" figure.
        public decimal LiquidGrossAssetsTWD { get; set; }
        // P&L only over positions that have a cost basis
        public decimal CostBasisTWD { get; set; }
        public decimal UnrealizedPnlTWD { get; set; }
        // % of LiquidGrossAssetsTWD, not of CostBasisTWD — this is "how big a move relative to my
        // liquid portfolio", not a return-on-cost figure.
        public decimal UnrealizedPnlPct { get; set; }
        // Σ Positions.EstimatedSellCostTWD — already netted into UnrealizedPnlTWD/TotalReturnPnlTWD/
        // AnnualizedReturnPct above, surfaced separately so the UI can show what was deducted.
        public decimal EstimatedSellCostTWD { get; set; }
        // Lifetime pre-tax cash dividends across the whole snapshot timeline. A SUPERSET of
        // Σ Positions.DividendsTWD: it also carries income from positions no longer held at all,
        // and from earlier closed holding periods of positions that are held again today. Income
        // earned during a period since closed was still really earned, so it stays counted here.
        public decimal TotalDividendsTWD { get; set; }
        // Total (含息) return: UnrealizedPnlTWD + TotalDividendsTWD.
        public decimal TotalReturnPnlTWD { get; set; }
        public decimal TotalReturnPct { get; set; }
        // Money-weighted annualized return (XIRR) over all snapshots, including dividend inflows;
        // null when not computable.
        public decimal? AnnualizedReturnPct { get; set; }
        // Single-day change in liquid net worth (流動淨資產 = liquid gross assets + liability,
        // excluding real estate) vs the previous trading day. Reflects cash/position changes between
        // snapshots as well as market price movements.
        public decimal? DayChangeTWD { get; set; }
        public decimal? DayChangePct { get; set; }
        // Net market exposure: Σ market value × exposure factor (leveraged ETFs amplify, bonds count as 0).
        public decimal MarketExposureTWD { get; set; }
        // 水位 — exposure ÷ liquid GROSS assets × 100: how much of what's actually held carries
        // market risk, ignoring how it was financed. Debt is deliberately not netted out; that's
        // what makes this a different reading from the leverage multiple below rather than a
        // rescaling of it. Null only when there are no liquid assets at all.
        public decimal? ExposureOfLiquidAssetsPct { get; set; }
        // 槓桿 — exposure ÷ liquid NET worth (liquid gross assets − liability), as a multiple
        // rather than a percentage: "15.0×" is read instantly where "1500%" needs converting, and
        // the different unit stops it being confused with the percentage above when the two sit
        // side by side. Real estate is excluded from the denominator: it can't be liquidated to
        // meet a margin call, so counting it would dilute exactly the risk this surfaces. Null
        // once liquid net worth hits 0 — the singularity sits precisely at insolvency, where this
        // explodes and then flips to a meaningless negative, so the UI shows 資不抵債 instead.
        // DrawdownBufferPct below is the reciprocal and stays readable through that region.
        public decimal? LiquidExposureMultiple { get; set; }
        // The reciprocal: liquid net worth ÷ exposure × 100 — "how far the market can fall before
        // liquid net worth reaches zero" (solve netWorth + exposure × f = 0). Well-behaved exactly
        // where the ratio above isn't: the denominator can't go negative, so this stays monotonic
        // and continuous straight through insolvency (negative = already underwater), and only
        // degenerates at zero exposure — the safest possible state, hence null → "無市場曝險".
        // Assumes all positions move together at beta 1; a conservative approximation, not precise.
        public decimal? DrawdownBufferPct { get; set; }
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

    /// <summary>
    /// One day's point on the drawdown curve — net worth relative to the running peak seen so far
    /// in the requested window. <see cref="DrawdownPct"/> is always ≤ 0 (0 on a new all-time high).
    /// </summary>
    public class DrawdownPointDto
    {
        public string Date { get; set; } = "";
        public decimal TotalTWD { get; set; }
        public decimal RunningPeakTWD { get; set; }
        public string RunningPeakDate { get; set; } = "";
        public decimal DrawdownPct { get; set; }
    }

    /// <summary>
    /// Max-drawdown analysis over a snapshot-price-history window: the single worst peak-to-trough
    /// decline, how long it took (or is taking) to recover, plus the full daily curve for charting.
    /// Priced off the same snapshot-timeline + historical-price-history mechanism as
    /// GetNetWorthHistoryAsync — quantities are held constant between snapshots, so this measures
    /// price-driven drawdown on the positions actually held, not a transaction-adjusted return.
    /// </summary>
    public class DrawdownAnalysisDto
    {
        public List<DrawdownPointDto> Points { get; set; } = [];
        // Negative (or 0 if the window never dipped below a prior peak).
        public decimal MaxDrawdownPct { get; set; }
        // Null when MaxDrawdownPct is 0 (no drawdown episode in the window).
        public string? PeakDate { get; set; }
        public string? TroughDate { get; set; }
        // Null while still underwater (net worth hasn't yet returned to PeakDate's value).
        public string? RecoveryDate { get; set; }
        // PeakDate → RecoveryDate (or → today if still underwater). Null alongside PeakDate.
        public int? UnderwaterDays { get; set; }
        public bool StillUnderwater { get; set; }
        // Today's drawdown from the window's running peak — independent of the worst-episode fields
        // above (e.g. can be 0 even if MaxDrawdownPct shows a large past episode that's since recovered).
        public decimal CurrentDrawdownPct { get; set; }
    }

    /// <summary>One benchmark's simulated "what if every contribution had bought this instead"
    /// curve — <see cref="ValuesTWD"/> is aligned index-for-index with
    /// <see cref="BenchmarkComparisonDto.Dates"/>/<see cref="BenchmarkComparisonDto.PortfolioTWD"/>.
    /// Each benchmark is simulated independently (its own unit-buying math at its own daily price),
    /// so different symbols never affect each other's curve.</summary>
    public class BenchmarkSeriesDto
    {
        public string Symbol { get; set; } = "";
        public List<decimal> ValuesTWD { get; set; } = [];
        // Time-weighted return over the window, in percent. NOT (last - first) / first: that
        // counted deposits as gains, so contributing 2.7M into a 7M portfolio read as "+31%"
        // having earned nothing. TWR removes each period's contribution from its opening base,
        // which is also what makes this directly comparable to the benchmark figure beside it.
        // 含息 (total return) on both sides: price-only would book a distribution as a loss on its
        // ex-date, penalising a high-yield portfolio against a lower-yielding benchmark that gives
        // up less price. Dividends count as income retained, not reinvested — matching 總覽.
        // Still a period return, not annualized — PortfolioSummaryDto.AnnualizedReturnPct (XIRR)
        // is the money-weighted, annualized figure and will legitimately differ.
        // NOTE: the plotted curves (PortfolioTWD / ValuesTWD) stay pure market value so they
        // reconcile against a brokerage statement, so they will not track this percentage.
        // Null when the first value is 0 (nothing to divide by).
        public decimal? ReturnPct { get; set; }
    }

    /// <summary>
    /// "What if every contribution I made had bought each of <see cref="Benchmarks"/> instead?" —
    /// every curve (portfolio + each benchmark) is scoped to the same universe
    /// PortfolioSummaryDto.AnnualizedReturnPct (XIRR) already uses: cost-tracked STOCK/FUND
    /// positions only, driven by the same cost-basis-delta contribution proxy. None of the curves
    /// include dividends, so the comparison is apples-to-apples (price return vs. price return),
    /// not a claim about total/含息 return. A date only appears in <see cref="Dates"/> when the
    /// portfolio AND every requested benchmark all had a price that day, so every array here stays
    /// the same length and index-aligned.
    /// </summary>
    public class BenchmarkComparisonDto
    {
        public List<string> Dates { get; set; } = [];
        public List<decimal> PortfolioTWD { get; set; } = [];
        public decimal? PortfolioReturnPct { get; set; }
        public List<BenchmarkSeriesDto> Benchmarks { get; set; } = [];
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
    /// verbatim); SubCategory is free-form and display-only; Tags (2-4) are free-form theme
    /// labels, sanitized/capped before persistence.</summary>
    public class CategoryClassificationResult
    {
        public required string Category { get; set; }
        public required string SubCategory { get; set; }
        public List<string>? Tags { get; set; }
    }
}
