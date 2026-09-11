import { green, red, blue, orange, grey } from '@mui/material/colors';
import { alpha } from '@mui/material/styles';

import type { SignalDirection, RiskLevel, IndicatorCategory } from '@/apis/financeApis/types';

/**
 * Solid colour at full opacity, or the same colour tinted when the caller wants a
 * translucent background. The tint is derived from the palette constant itself, so the
 * two variants can never drift apart the way the hand-written `rgba(...)` literals this
 * replaces had already started to.
 */
const withOpacity = (color: string, opacity: number): string =>
  opacity === 1 ? color : alpha(color, opacity);

const TREND_COLORS: Record<string, string> = {
  bullish: green[500],
  bearish: red[500],
};

/**
 * Get trend color with optional opacity
 */
export const getTrendColor = (trend: string, opacity: number = 1): string =>
  withOpacity(TREND_COLORS[trend] ?? blue[500], opacity);

/**
 * Get trend label in Chinese
 */
export const getTrendLabel = (trend: string): string => {
  switch (trend) {
    case 'bullish':
      return '看漲';
    case 'bearish':
      return '看跌';
    default:
      return '中性';
  }
};

const RECOMMENDATION_COLORS: Record<string, string> = {
  buy: green[500],
  sell: red[500],
};

/**
 * Get recommendation color with optional opacity
 */
export const getRecommendationColor = (recommendation: string, opacity: number = 1): string =>
  withOpacity(RECOMMENDATION_COLORS[recommendation] ?? orange[500], opacity);

/**
 * Get recommendation label in Chinese
 */
export const getRecommendationLabel = (recommendation: string): string => {
  switch (recommendation) {
    case 'buy':
      return '建議買進';
    case 'sell':
      return '建議賣出';
    default:
      return '建議持有';
  }
};

/**
 * Get MUI chip color for recommendation
 */
export const getRecommendationChipColor = (
  recommendation: string,
): 'success' | 'error' | 'warning' => {
  switch (recommendation) {
    case 'buy':
      return 'success';
    case 'sell':
      return 'error';
    default:
      return 'warning';
  }
};

/**
 * Get confidence label in Chinese
 */
export const getConfidenceLabel = (confidence: string): string => {
  switch (confidence) {
    case 'high':
      return '高';
    case 'medium':
      return '中';
    case 'low':
      return '低';
    default:
      return '未知';
  }
};

// === Comprehensive Analysis Helpers ===

export const getDirectionLabel = (d: SignalDirection): string => {
  const map: Record<SignalDirection, string> = {
    StrongBullish: '強勢看多',
    Bullish: '偏多',
    Neutral: '中性',
    Bearish: '偏空',
    StrongBearish: '強勢看空',
  };
  return map[d] ?? '未知';
};

export const getDirectionChipColor = (d: SignalDirection): 'success' | 'error' | 'default' => {
  if (d === 'StrongBullish' || d === 'Bullish') return 'success';
  if (d === 'Bearish' || d === 'StrongBearish') return 'error';
  return 'default';
};

const DIRECTION_COLORS: Record<SignalDirection, string> = {
  StrongBullish: green[700],
  Bullish: green[500],
  Neutral: grey[500],
  Bearish: red[500],
  StrongBearish: red[700],
};

export const getDirectionColor = (d: SignalDirection, opacity = 1): string =>
  withOpacity(DIRECTION_COLORS[d] ?? blue[500], opacity);

export const getRiskLabel = (l: RiskLevel): string => {
  const map: Record<RiskLevel, string> = {
    Low: '低風險',
    Medium: '中等風險',
    High: '高風險',
    VeryHigh: '極高風險',
  };
  return map[l] ?? '未知';
};

export const getRiskChipColor = (l: RiskLevel): 'success' | 'warning' | 'error' | 'default' => {
  if (l === 'Low') return 'success';
  if (l === 'Medium') return 'warning';
  return 'error';
};

export const getRiskColor = (l: RiskLevel): string => {
  const map: Record<RiskLevel, string> = {
    Low: green[500],
    Medium: orange[500],
    High: red[500],
    VeryHigh: red[900],
  };
  return map[l] ?? grey[500];
};

export const getCategoryLabel = (c: IndicatorCategory): string => {
  const map: Record<IndicatorCategory, string> = {
    Technical: '技術面',
    Chip: '籌碼面',
    Fundamental: '基本面',
  };
  return map[c] ?? c;
};

export const getScoreColor = (score: number): string => {
  if (score >= 70) return green[500];
  if (score >= 50) return orange[500];
  if (score >= 30) return orange[700];
  return red[500];
};

export const getIndicatorDisplayName = (name: string): string => {
  const map: Record<string, string> = {
    // Technical
    MA: '均線 (MA)',
    RSI: '相對強弱指標 (RSI)',
    MACD: 'MACD',
    KD: 'KD 隨機指標',
    BollingerBands: '布林通道',
    VolumeRatio: '量能比',
    // Chip
    MarginIndicator: '融資融券',
    ForeignHolding: '外資持股',
    DirectorPledge: '董監設質',
    // Fundamental
    PERatio: '本益比 (P/E)',
    PBRatio: '股價淨值比 (P/B)',
    DividendYield: '殖利率',
    RevenueGrowth: '月營收成長',
    EPS: '每股盈餘 (EPS)',
  };
  return map[name] ?? name;
};
