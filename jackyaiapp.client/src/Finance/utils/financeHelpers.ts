import { green, red, blue, orange } from '@mui/material/colors';

/**
 * Get the current date in YYYY-MM-DD format
 */
export const getCurrentDate = (): string => {
  const date = new Date();
  return date.toISOString().split('T')[0];
};

/**
 * Get avatar color based on market implication
 */
export const getImplicationColor = (implication: string): string => {
  switch (implication) {
    case 'bullish':
      return green[500];
    case 'bearish':
      return red[500];
    default:
      return blue[500];
  }
};

/**
 * Get trend color with optional alpha
 */
export const getTrendColor = (trend: string, alpha: number = 1): string => {
  switch (trend) {
    case 'bullish':
      return alpha === 1 ? green[500] : `rgba(76, 175, 80, ${alpha})`;
    case 'bearish':
      return alpha === 1 ? red[500] : `rgba(244, 67, 54, ${alpha})`;
    default:
      return alpha === 1 ? blue[500] : `rgba(33, 150, 243, ${alpha})`;
  }
};

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

/**
 * Get recommendation color with optional alpha
 */
export const getRecommendationColor = (recommendation: string, alpha: number = 1): string => {
  switch (recommendation) {
    case 'buy':
      return alpha === 1 ? green[500] : `rgba(76, 175, 80, ${alpha})`;
    case 'sell':
      return alpha === 1 ? red[500] : `rgba(244, 67, 54, ${alpha})`;
    default:
      return alpha === 1 ? orange[500] : `rgba(255, 152, 0, ${alpha})`;
  }
};

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
export const getRecommendationChipColor = (recommendation: string): 'success' | 'error' | 'warning' => {
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

/**
 * Get implication chip color for MUI Chip
 */
export const getImplicationChipColor = (implication: string): 'success' | 'error' | 'info' => {
  switch (implication) {
    case 'bullish':
      return 'success';
    case 'bearish':
      return 'error';
    default:
      return 'info';
  }
};

/**
 * Get implication label in Chinese
 */
export const getImplicationLabel = (implication: string): string => {
  switch (implication) {
    case 'bullish':
      return '看漲';
    case 'bearish':
      return '看跌';
    default:
      return '中性';
  }
};
