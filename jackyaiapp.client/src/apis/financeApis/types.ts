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
