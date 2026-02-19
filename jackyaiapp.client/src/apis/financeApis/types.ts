// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\apis\financeApis\types.ts

export interface StrategicInsight {
  stockCode: string;
  companyName: string;
  date: string;
  title: string;
  summary: string;
  implication: 'bullish' | 'bearish' | 'neutral';
  suggestedAction: string | null;
  rawText: string;
}

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

export interface StockAnalysisResultData {
  stockCode: string;
  companyName: string;
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
