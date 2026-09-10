import { BarChart, LineChart, PieChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import * as echarts from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';

import type {
  BarSeriesOption,
  LineSeriesOption,
  PieSeriesOption,
  SankeySeriesOption,
  SunburstSeriesOption,
  TreeSeriesOption,
  TreemapSeriesOption,
} from 'echarts/charts';
import type {
  GridComponentOption,
  LegendComponentOption,
  TooltipComponentOption,
} from 'echarts/components';
import type { ComposeOption } from 'echarts/core';

// ── ECharts registration (once at module load) ────────────────────────────────
//
// Shared by every ECharts consumer in the app (DataAnalysis, Portfolio, Finance) — not a
// DataAnalysis internal, even though it originally lived under
// DataAnalysis/components/summarizeChart/. DataAnalysis's own view-type concepts
// (ChartViewType/ActiveChart/PALETTE/CHART_HEIGHT/...) stayed behind in that file, since only this
// registration + theme + ECOption type are actually cross-feature.
//
// Only the chart types *every* charting route can draw belong here: this list is what lands in the
// shared ECharts chunk that Portfolio, Finance and DataAnalysis all pull in. A chart type used by a
// single feature is registered by that feature instead, so the other routes don't download it —
// see DataAnalysis/viz/registerDataAnalysisCharts.ts for treemap/sunburst/tree/sankey.
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
  | TreemapSeriesOption
  | SunburstSeriesOption
  | TreeSeriesOption
  | SankeySeriesOption
  | GridComponentOption
  | LegendComponentOption
  | TooltipComponentOption
>;
