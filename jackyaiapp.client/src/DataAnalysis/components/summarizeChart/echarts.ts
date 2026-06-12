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

// ── Custom dark theme matched to the DataAnalysis page design tokens ─────────
// (ECharts' built-in "dark" theme brings its own navy background and palette,
// which clashes with the page's near-black Linear-style surfaces.)

const TEXT_MUTED = '#9B9DA5';
const TEXT_BRIGHT = '#EEEFF1';
const AXIS_LINE = 'rgba(255, 255, 255, 0.15)';
const SPLIT_LINE = 'rgba(255, 255, 255, 0.07)';

export const ECHARTS_THEME = 'data-analysis-dark';

const axisCommon = {
  axisLine: { lineStyle: { color: AXIS_LINE } },
  axisTick: { lineStyle: { color: AXIS_LINE } },
  axisLabel: { color: TEXT_MUTED },
  splitLine: { lineStyle: { color: SPLIT_LINE } },
  nameTextStyle: { color: TEXT_MUTED },
};

echarts.registerTheme(ECHARTS_THEME, {
  darkMode: true,
  backgroundColor: 'transparent',
  textStyle: { color: TEXT_MUTED },
  categoryAxis: axisCommon,
  valueAxis: axisCommon,
  logAxis: axisCommon,
  timeAxis: axisCommon,
  legend: {
    textStyle: { color: TEXT_MUTED },
    pageTextStyle: { color: TEXT_MUTED },
    pageIconColor: TEXT_BRIGHT,
    pageIconInactiveColor: 'rgba(255, 255, 255, 0.25)',
    inactiveColor: 'rgba(255, 255, 255, 0.25)',
  },
  tooltip: {
    backgroundColor: '#26272D',
    borderColor: 'rgba(255, 255, 255, 0.08)',
    textStyle: { color: TEXT_BRIGHT },
  },
});

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
