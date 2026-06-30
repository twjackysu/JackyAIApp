export type AssetType = 'STOCK' | 'CASH' | 'FUND';
export type Market = 'TW' | 'US';

export interface PositionInput {
  assetType: AssetType;
  symbol: string;
  name: string;
  market: string;
  currency: string;
  quantity: number;
  unitPrice?: number | null; // fund NAV
  costBasis?: number | null; // optional, stocks only
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
  quantity: number;
  currentPrice: number;
  marketValueLocal: number;
  marketValueTWD: number;
  costBasisTWD?: number | null;
  unrealizedPnlTWD?: number | null;
  unrealizedPnlPct?: number | null;
  priceFetchFailed: boolean;
}

export interface PortfolioSummaryDto {
  effectiveDate?: string | null;
  usdTwdRate: number;
  totalNetWorthTWD: number;
  twStockTWD: number;
  usStockTWD: number;
  cashTWD: number;
  fundTWD: number;
  costBasisTWD: number;
  unrealizedPnlTWD: number;
  unrealizedPnlPct: number;
  positions: PositionDto[];
}

export interface NetWorthDataPoint {
  date: string;
  totalTWD: number;
  twStockTWD: number;
  usStockTWD: number;
  cashTWD: number;
  fundTWD: number;
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

export interface HoldingPriceHistoryDto {
  symbol: string;
  companyName: string;
  market: Market;
  currency: string;
  quantity: number;
  avgCost?: number | null;
  prices: PricePoint[];
}
