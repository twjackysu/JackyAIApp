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
  exposureRatioPct: number; // exposure ÷ net worth (incl. real estate) × 100
  // Same numerator, denominator excludes realEstateTWD — how leveraged the liquid portfolio alone is.
  liquidExposureRatioPct: number;
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
