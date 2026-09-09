// Registration, dark theme, and ECOption live in @/charts/echarts (shared across DataAnalysis,
// Portfolio, and Finance) — the theme and type are re-exported here so this file's own consumers
// (the viz renderers in DataAnalysis/viz/renderers/) don't need to change their import path. The
// registered `echarts` instance itself is not: it is only ever touched by @/charts/ReactECharts,
// which every chart in the app renders through. Only DataAnalysis's own view-type concepts are
// defined in this file.
export { ECHARTS_THEME } from '@/charts/echarts';
export type { ECOption } from '@/charts/echarts';

export type ChartViewType =
  | 'table'
  | 'column' // vertical bars
  | 'bar' // horizontal bars
  | 'line'
  | 'pie'
  | 'stackedColumn'
  | 'stackedBar';

export type ActiveChart = Exclude<ChartViewType, 'table'>;

export const PALETTE = [
  '#5E6AD2',
  '#22d3ee',
  '#f59e0b',
  '#10b981',
  '#f43f5e',
  '#8b5cf6',
  '#0ea5e9',
  '#ec4899',
  '#84cc16',
  '#f97316',
  '#06b6d4',
  '#a855f7',
];

export const CHART_HEIGHT = 400;
export const ANGLE_THRESHOLD = 30; // rotate X labels when > this many categories
export const CLUTTER_THRESHOLD = 80; // warn when > this many rows
