import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';

import type { BarSeriesOption, LineSeriesOption, PieSeriesOption } from 'echarts/charts';
import type {
  GridComponentOption,
  LegendComponentOption,
  TooltipComponentOption,
} from 'echarts/components';
import type { ComposeOption } from 'echarts/core';

// ── ECharts registration (once at module load) ────────────────────────────────
echarts.use([
  BarChart,
  LineChart,
  PieChart,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  CanvasRenderer,
]);

export { echarts };

export type ECOption = ComposeOption<
  | BarSeriesOption
  | LineSeriesOption
  | PieSeriesOption
  | GridComponentOption
  | LegendComponentOption
  | TooltipComponentOption
>;

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
  '#6366f1',
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
