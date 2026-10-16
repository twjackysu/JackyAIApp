import { CandlestickChart } from 'echarts/charts';

import type { CandlestickSeriesOption } from 'echarts/charts';
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

export type CandlestickChartOption = ComposeOption<
  CandlestickSeriesOption | GridComponentOption | TooltipComponentOption
>;
