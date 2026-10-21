// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\apis\financeApis\types.ts

export interface StockTrendAnalysis {
  stockCode: string; // Stock code, e.g., "2330"
  companyName: string; // Company name, e.g., "台積電"
  currentPrice?: number; // Current stock price
  shortTermTrend: 'bullish' | 'bearish' | 'neutral'; // Short-term trend (1-3 months)
  mediumTermTrend: 'bullish' | 'bearish' | 'neutral'; // Medium-term trend (3-12 months)
  longTermTrend: 'bullish' | 'bearish' | 'neutral'; // Long-term trend (1-3 years)
  shortTermSummary: string; // Short-term analysis summary
  mediumTermSummary: string; // Medium-term analysis summary
  longTermSummary: string; // Long-term analysis summary
  keyFactors: string[]; // Key factors affecting the trends
  riskFactors: string[]; // Risk factors to consider
  recommendation: 'buy' | 'sell' | 'hold'; // Investment recommendation
  confidenceLevel: 'high' | 'medium' | 'low'; // Confidence in the analysis
  lastUpdated: string; // Analysis timestamp
  dataSource: string; // Source of the analysis data
}

export interface StockSearchRequest {
  stockCodeOrName: string; // Stock code or company name to search
}

// === Indicator & Scoring Types ===

export type IndicatorCategory = 'Technical' | 'Chip' | 'Fundamental';
export type SignalDirection = 'StrongBullish' | 'Bullish' | 'Neutral' | 'Bearish' | 'StrongBearish';
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'VeryHigh';

export interface IndicatorResult {
  name: string;
  category: IndicatorCategory;
  value: number;
  subValues: Record<string, number>;
  signal: string;
  direction: SignalDirection;
  score: number;
  reason: string;
}

export interface CategoryScore {
  category: IndicatorCategory;
  score: number;
  weight: number;
  weightedScore: number;
  direction: SignalDirection;
  summary: string;
  indicatorCount: number;
}

export interface RiskAssessment {
  level: RiskLevel;
  factors: string[];
  divergenceScore: number;
}

export interface StockScoreResponse {
  stockCode: string;
  companyName: string;
  latestClose: number | null;
  overallScore: number;
  overallDirection: SignalDirection;
  recommendation: string;
  categoryScores: CategoryScore[];
  indicators: IndicatorResult[];
  risk: RiskAssessment;
  dataRange: string;
  generatedAt: string;
}

export type MarketRegion = 'TW' | 'US';

export interface StockAnalysisResultData {
  stockCode: string;
  companyName: string;
  market: MarketRegion;
  latestClose: number | null;
  indicators: IndicatorResult[];
  scoring: StockScoreResponse | null;
  risk: RiskAssessment | null;
  dataRange: string;
  configuration: {
    includeTechnical: boolean;
    includeChip: boolean;
    includeFundamental: boolean;
    includeScoring: boolean;
    includeRisk: boolean;
    onlyIndicators: string[];
    excludeIndicators: string[];
  };
  generatedAt: string;
}

export interface StockAnalysisRequest {
  stockCode: string;
  market?: MarketRegion;
  includeTechnical?: boolean;
  includeChip?: boolean;
  includeFundamental?: boolean;
  includeScoring?: boolean;
  includeRisk?: boolean;
  onlyIndicators?: string[];
  excludeIndicators?: string[];
  technicalWeight?: number;
  chipWeight?: number;
  fundamentalWeight?: number;
}

// === Macro Economy Types ===

export interface MarketIndexDay {
  date: string;
  taiex: number;
  change: number;
  tradeVolume: number;
  tradeValue: number;
  transaction: number;
}

export interface SectorIndex {
  // 民國 date string (e.g. "1150211") — TWSE's feed only publishes the most recently finalized
  // trading day, which can lag "today" by a day or more; format with formatROCDate for display.
  date: string;
  name: string;
  closeIndex: number;
  direction: string;
  changePoints: number;
  changePercent: number;
}

export interface MarginSummary {
  marginBuyTotal: number;
  marginSellTotal: number;
  marginBalanceTotal: number;
  marginPrevBalanceTotal: number;
  shortSellTotal: number;
  shortBuyTotal: number;
  shortBalanceTotal: number;
  shortPrevBalanceTotal: number;
  /** Today's balance − the previous day's (includes repayments); null when no previous balance. */
  marginChange: number | null;
  shortChange: number | null;
}

export interface ExchangeRate {
  currency: string;
  displayName: string;
  buyRate: number | null;
  sellRate: number | null;
}

export interface BankRate {
  bankName: string;
  period: string;
  oneYearFixed: number | null;
  oneYearFloating: number | null;
  baseLendingRate: number | null;
}

export interface MacroEconomyData {
  marketIndex: MarketIndexDay[];
  sectorIndices: SectorIndex[];
  margin: MarginSummary | null;
  exchangeRates: ExchangeRate[];
  bankRate: BankRate | null;
  generatedAt: string;
}

// === Market Overview (dashboard) Types ===

export type MarketGroup = 'US' | 'TW' | 'FX' | 'Other';

export interface MarketQuote {
  symbol: string;
  name: string;
  group: MarketGroup | string;
  price: number | null;
  change: number | null;
  changePercent: number | null;
}

export interface MarketOverviewData {
  quotes: MarketQuote[];
  generatedAt: string;
}

// === Market Digest (今日市場AI摘要) Types ===

export interface DigestPoint {
  category: 'global' | 'taiwan' | string;
  headline: string;
  detail: string;
  sentiment: 'bullish' | 'bearish' | 'neutral' | string;
  stockCode?: string | null;
}

export interface MarketDigestData {
  points: DigestPoint[];
  generatedAt: string;
}

// === Indicator Explorer Types ===

export interface IndicatorPoint {
  date: string;
  value: number;
}

export interface IndicatorSeries {
  id: string;
  name: string;
  description: string;
  unit: string;
  latestValue: number | null;
  latestDate: string | null;
  previousValue: number | null;
  points: IndicatorPoint[];
}

export interface IndicatorExplorerData {
  indicators: IndicatorSeries[];
  generatedAt: string;
}

export interface IndicatorInsightData {
  insight: string;
}

// === Market Index History (過去 5 年同期間走勢圖) Types ===

export interface MarketIndexPoint {
  /** Trading day, `YYYY-MM-DD` (Asia/Taipei), ascending. */
  date: string;
  close: number;
}

export interface MarketIndexHistoryData {
  symbol: string;
  name: string;
  source: string;
  /** Last trading day in `points`. */
  asOf: string;
  points: MarketIndexPoint[];
}

// === Price Bars (趨勢線 K 線圖) Types ===

export interface PriceBarsRequest {
  /** A stock code or company name, or `TAIEX` for the weighted index's last six years. */
  code: string;
  market?: MarketRegion;
}

export interface PriceBar {
  /** `YYYY-MM-DD`, ascending. */
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  /** Shares traded (股, not 張). Absent for the TAIEX and for a day the source has none for. */
  volume?: number;
}

export interface PriceBarsData {
  /** Code as resolved, e.g. `2330`, `AAPL`, or `TAIEX`. */
  code: string;
  name: string;
  asOf: string;
  bars: PriceBar[];
}
