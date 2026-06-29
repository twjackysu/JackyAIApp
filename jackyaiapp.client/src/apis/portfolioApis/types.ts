export type TransactionType = 'BUY' | 'SELL' | 'DIVIDEND';
export type Market = 'TW' | 'US';

export interface AddTransactionRequest {
  symbol: string;
  market: Market;
  companyName: string;
  type: TransactionType;
  date: string; // ISO date string
  quantity: number;
  price: number;
  currency: string;
  fee: number;
  note?: string;
}

export interface TransactionDto {
  id: number;
  symbol: string;
  market: Market;
  companyName: string;
  type: TransactionType;
  date: string;
  quantity: number;
  price: number;
  currency: string;
  fee: number;
  note?: string;
}

export interface HoldingDto {
  symbol: string;
  market: Market;
  companyName: string;
  currency: string;
  quantity: number;
  avgCost: number;
  currentPrice: number;
  marketValueLocal: number;
  marketValueTWD: number;
  costBasisTWD: number;
  unrealizedPnlTWD: number;
  unrealizedPnlPct: number;
  priceFetchFailed: boolean;
}

export interface PortfolioSummaryDto {
  totalNetWorthTWD: number;
  twStockTWD: number;
  usStockTWD: number;
  usdTwdRate: number;
  totalUnrealizedPnlTWD: number;
  totalUnrealizedPnlPct: number;
  holdings: HoldingDto[];
}
