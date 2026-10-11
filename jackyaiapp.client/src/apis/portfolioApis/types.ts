export type AssetType = 'STOCK' | 'CASH' | 'FUND' | 'LIABILITY' | 'REAL_ESTATE';
export type Market = 'TW' | 'US';
export type ValuationMethod = '市價' | '買價扣除折舊';

export interface PositionInput {
  assetType: AssetType;
  symbol: string;
  name: string;
  market: string;
  currency: string;
  costCurrency?: string | null; // currency the cost was paid in; defaults to currency
  quantity: number;
  unitPrice?: number | null; // fund NAV
  costBasis?: number | null; // optional, stocks only
  valuationMethod?: string | null; // REAL_ESTATE only
}

export interface SaveHoldingsRequest {
  effectiveDate?: string | null; // yyyy-MM-dd
  positions: PositionInput[];
}

export interface PositionDto {
  assetType: AssetType;
  symbol: string;
  name: string;
  market: string;
  currency: string;
  costCurrency?: string | null;
  quantity: number;
  currentPrice: number;
  // Negative for LIABILITY (amount owed); positive for everything else.
  marketValueLocal: number;
  marketValueTWD: number;
  costBasisTWD?: number | null;
  unrealizedPnlTWD?: number | null;
  unrealizedPnlPct?: number | null;
  exposureFactor: number;
  // Rate this position's foreign currency was converted to TWD at; null for TWD holdings.
  // Not one number app-wide: funds settle through 集保 at its published rate (what 基富通 shows),
  // US stocks through 國泰世華's board rate for the quote's own day (what 國泰複委託 shows), and
  // anything else falls back to mid-market. They differ enough to matter when reconciling.
  fxRateUsed?: number | null;
  fxRateSource?: 'TDCC' | 'CATHAY' | 'MID' | null;
  priceFetchFailed: boolean;
  // Cumulative pre-tax cash dividends earned while holding this position (0 = none/not applicable).
  dividendsTWD: number;
  // Estimated brokerage fee + transaction tax if sold at the current price (TW STOCK only,
  // 0 otherwise). Already subtracted out of unrealizedPnlTWD/Pct above.
  estimatedSellCostTWD: number;
  // REAL_ESTATE only: "市價" | "買價扣除折舊", null otherwise.
  valuationMethod?: string | null;
  // AI theme tags (0-4, e.g. ["雲端","AI"]) — empty until the symbol has been classified (the
  // 產業/子類別 chart tabs trigger that lazily). Display-only; never summed/stacked.
  tags: string[];
}

export interface PortfolioSummaryDto {
  effectiveDate?: string | null;
  usdTwdRate: number;
  // Net of liabilities.
  totalNetWorthTWD: number;
  twStockTWD: number;
  usStockTWD: number;
  cashTWD: number;
  fundTWD: number;
  // User-entered real-estate value (see PositionDto.valuationMethod). Zero market exposure but
  // counts toward totalNetWorthTWD — so the exposure ratio isn't inflated by ignoring it.
  realEstateTWD: number;
  // Total amount owed, positive. Already subtracted out of totalNetWorthTWD.
  liabilityTWD: number;
  // twStockTWD + usStockTWD + cashTWD + fundTWD — excludes real estate, NOT net of liability.
  liquidGrossAssetsTWD: number;
  costBasisTWD: number;
  unrealizedPnlTWD: number;
  unrealizedPnlPct: number;
  // Σ positions.estimatedSellCostTWD — already netted into unrealizedPnlTWD/totalReturnPnlTWD/
  // annualizedReturnPct above, surfaced separately so the UI can show what was deducted.
  estimatedSellCostTWD: number;
  // Cumulative pre-tax cash dividends across the whole snapshot timeline.
  totalDividendsTWD: number;
  // Total (含息) return: unrealizedPnlTWD + totalDividendsTWD.
  totalReturnPnlTWD: number;
  totalReturnPct: number;
  // XIRR — includes dividend inflows (含息).
  annualizedReturnPct?: number | null;
  marketExposureTWD: number;
  // 水位 — exposure ÷ liquid GROSS assets × 100: how much of what's held carries market risk,
  // ignoring how it was financed. Null only when there are no liquid assets at all.
  exposureOfLiquidAssetsPct: number | null;
  // 槓桿 — exposure ÷ liquid NET worth, as a multiple (15.0× reads instantly where 1500% needs
  // converting, and the different unit keeps it distinct from the percentage above). Real estate
  // is excluded: it can't be liquidated to meet a margin call. Null once liquid net worth reaches
  // 0 — the singularity sits exactly at insolvency, where it explodes then flips to a meaningless
  // negative, so render 資不抵債 instead of a number.
  liquidExposureMultiple: number | null;
  // The reciprocal: liquid net worth ÷ exposure × 100 — "抗跌空間", how far the market can fall
  // before liquid net worth hits zero. Stays monotonic and continuous through insolvency
  // (negative = already underwater); null only at zero exposure (nothing at risk).
  drawdownBufferPct: number | null;
  dayChangeTWD?: number | null;
  dayChangePct?: number | null;
  positions: PositionDto[];
}

export type NetWorthGroupBy = 'assetType' | 'category' | 'currency' | 'subCategory' | 'holding';

export interface NetWorthGroupValue {
  key: string;
  valueTWD: number;
}

/**
 * One day's breakdown, grouped by whatever `groupBy` the request asked for. `groups[].key`
 * format depends on the mode:
 * - assetType: "TwStock" | "UsStock" | "Cash" | "Fund" | "Liability"
 * - category: a PortfolioCategory name (e.g. "Semiconductor") — see CATEGORY_LABELS/CATEGORY_COLORS
 * - currency: "TWD" | "USD"
 * Group values are signed and sum to totalTWD — Liability/debt is negative.
 */
export interface NetWorthDataPoint {
  date: string;
  totalTWD: number;
  groups: NetWorthGroupValue[];
}

/** One day's point on the drawdown curve — net worth vs. the running peak seen so far in the
 * requested window. drawdownPct is always <= 0 (0 on a new all-time high). */
export interface DrawdownPoint {
  date: string;
  totalTWD: number;
  runningPeakTWD: number;
  runningPeakDate: string;
  drawdownPct: number;
}

/** Max-drawdown analysis: the single worst peak-to-trough episode in the window, plus the full
 * daily curve. peakDate/troughDate/recoveryDate/underwaterDays are all null together when
 * maxDrawdownPct is 0 (no drawdown episode occurred in the window). */
export interface DrawdownAnalysis {
  points: DrawdownPoint[];
  maxDrawdownPct: number;
  peakDate: string | null;
  troughDate: string | null;
  recoveryDate: string | null;
  underwaterDays: number | null;
  stillUnderwater: boolean;
  currentDrawdownPct: number;
}

/** One benchmark's simulated "what if every contribution had bought this instead" curve —
 * valuesTWD is index-aligned with BenchmarkComparison.dates/portfolioTWD. */
export interface BenchmarkSeries {
  symbol: string;
  valuesTWD: number[];
  returnPct: number | null;
}

/** "What if every contribution had bought each of benchmarks instead?" Every curve (portfolio +
 * each benchmark) is scoped to the same cost-tracked STOCK/FUND universe as
 * PortfolioSummaryDto.annualizedReturnPct (XIRR), and none include dividends — a price-return vs.
 * price-return comparison, not a 含息 claim. dates/portfolioTWD/each benchmark's valuesTWD are all
 * the same length and index-aligned. */
export interface BenchmarkComparison {
  dates: string[];
  portfolioTWD: number[];
  portfolioReturnPct: number | null;
  benchmarks: BenchmarkSeries[];
}

export interface UpdateHistoryItem {
  snapshotId: number;
  effectiveDate: string;
  positionCount: number;
  usdTwdRate: number;
}

export interface StockLookupDto {
  symbol: string;
  companyName: string;
  currentPrice: number;
  currency: string;
}

export interface FundSearchItem {
  fundId: string;
  name: string;
  isSell: boolean;
}

export interface StockSearchItem {
  symbol: string;
  name: string;
  market: string;
}

export interface PricePoint {
  date: string;
  close: number;
  high: number;
  low: number;
}

export interface PortfolioAiSummaryDto {
  trendLabel: string;
  action: string;
  signals: string;
}

export interface HoldingPriceHistoryDto {
  symbol: string;
  companyName: string;
  market: Market;
  currency: string;
  quantity: number;
  avgCost?: number | null;
  prices: PricePoint[];
}

// 回落煞車 (Issue #431): market-wide signal, identical for every user. Contract in
// ARCHITECTURE.md「回落煞車」. Percent values are `...Pct` like every other DTO here.
export type DrawdownBrakeState = 'OFFENSE' | 'DEFENSE';

// Phase 2 (TASK-038) — always null until the backend ships intraday estimates.
export interface DrawdownBrakeEstimateDto {
  asOfTime: string; // ISO 8601
  estimatedClose: number;
  state: DrawdownBrakeState;
  prevState: DrawdownBrakeState | null;
  drawdownPct: number;
}

export interface DrawdownBrakeDto {
  // Latest official (closed) trading day the signal was computed from.
  asOfDate: string | null; // yyyy-MM-dd
  // Every nullable field below is null while `isInitializing` (table empty / backfilling).
  state: DrawdownBrakeState | null;
  // State on the trading day before asOfDate; null on the very first day of the series.
  prevState: DrawdownBrakeState | null;
  lastSwitchDate: string | null; // yyyy-MM-dd
  close: number | null;
  high252: number | null;
  sma60: number | null;
  // close / high252 - 1, as a percent (e.g. -1.2).
  drawdownPct: number | null;
  // Room left before the -8% trigger, in percentage points; < 0 once already below the line.
  distanceToTriggerPct: number | null;
  // A new close should exist by now but does not (market holidays do not count).
  isStale: boolean;
  isInitializing: boolean;
  estimate: DrawdownBrakeEstimateDto | null;
  // The earlier intraday estimate's state differs from the official one.
  estimateCorrected: boolean;
}
