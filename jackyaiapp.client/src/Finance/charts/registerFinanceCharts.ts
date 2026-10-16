import { CandlestickChart } from 'echarts/charts';

import type { BarSeriesOption, CandlestickSeriesOption } from 'echarts/charts';
import type { GridComponentOption, TooltipComponentOption } from 'echarts/components';
import type { ComposeOption } from 'echarts/core';

import { echarts } from '@/charts/echarts';

// ── Finance-only ECharts registration (side effect on import) ─────────────────
//
// The candlestick chart is only drawn by Stock Intelligence's trendline chart (the trendlines
// themselves are an SVG overlay the user can drag). Registering it here, not in @/charts/echarts,
// keeps it out of the chunk Portfolio and DataAnalysis download (see
// DataAnalysis/viz/registerDataAnalysisCharts.ts).
echarts.use([CandlestickChart]);

/** Candlesticks, with a volume pane of bars (registered app-wide) when there is volume. */
export type CandlestickChartOption = ComposeOption<
  CandlestickSeriesOption | BarSeriesOption | GridComponentOption | TooltipComponentOption
>;
