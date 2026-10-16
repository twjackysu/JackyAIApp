import { CandlestickChart } from 'echarts/charts';
import { MarkLineComponent } from 'echarts/components';

import type { CandlestickSeriesOption } from 'echarts/charts';
import type {
  GridComponentOption,
  MarkLineComponentOption,
  TooltipComponentOption,
} from 'echarts/components';
import type { ComposeOption } from 'echarts/core';

import { echarts } from '@/charts/echarts';

// ── Finance-only ECharts registration (side effect on import) ─────────────────
//
// The candlestick chart and its trendlines (mark lines) are only drawn by Stock Intelligence's
// trendline chart. Registering them here, not in @/charts/echarts, keeps them out of the chunk
// Portfolio and DataAnalysis download (see DataAnalysis/viz/registerDataAnalysisCharts.ts).
echarts.use([CandlestickChart, MarkLineComponent]);

export type CandlestickChartOption = ComposeOption<
  CandlestickSeriesOption | GridComponentOption | TooltipComponentOption | MarkLineComponentOption
>;
