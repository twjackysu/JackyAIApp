import { lineAt, type OhlcBar, type Trendline, type TrendlineAnalysis } from './trendlines';

import type { CandlestickChartOption } from '../charts/registerFinanceCharts';

/** Taiwan convention: red up, green down. */
export const UP_COLOR = '#EF5350';
export const DOWN_COLOR = '#26A69A';
export const SUPPORT_COLOR = '#42A5F5';
export const RESISTANCE_COLOR = '#BA68C8';

const lineColor = (l: Trendline) => (l.side === 'support' ? SUPPORT_COLOR : RESISTANCE_COLOR);

/**
 * Candlesticks with each trendline drawn as a mark line from its first swing point to where it
 * ended. Lines still in force are drawn bolder; the rest are history.
 */
export function buildTrendlineOption(
  bars: OhlcBar[],
  analysis: TrendlineAnalysis,
): CandlestickChartOption {
  const dates = bars.map((b) => b.date);
  return {
    animation: false,
    grid: { left: 8, right: 64, top: 16, bottom: 28 },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' },
      valueFormatter: (v) => (typeof v === 'number' ? v.toLocaleString('zh-TW') : String(v)),
    },
    xAxis: { type: 'category', data: dates, boundaryGap: true, axisLabel: { hideOverlap: true } },
    yAxis: { type: 'value', scale: true, position: 'right', splitNumber: 6 },
    series: [
      {
        type: 'candlestick',
        name: '日K',
        // ECharts' candlestick order: open, close, low, high.
        data: bars.map((b) => [b.open, b.close, b.low, b.high]),
        itemStyle: {
          color: UP_COLOR,
          color0: DOWN_COLOR,
          borderColor: UP_COLOR,
          borderColor0: DOWN_COLOR,
        },
        markLine: {
          symbol: ['none', 'none'],
          silent: true,
          label: { show: false },
          data: analysis.lines.map((l) => [
            {
              coord: [dates[l.start], lineAt(l, l.start)],
              lineStyle: {
                color: lineColor(l),
                width: l.isActive ? 2.5 : 1.5,
                type: 'solid',
                opacity: l.isActive ? 1 : 0.7,
              },
            },
            { coord: [dates[l.end], lineAt(l, l.end)] },
          ]),
        },
      },
    ],
  };
}

/** Where the last close sits against a line's value today, e.g. "+1.2%". */
export function distancePct(lastClose: number, level: number): string {
  const pct = (lastClose / level - 1) * 100;
  return `${pct >= 0 ? '+' : ''}${pct.toFixed(1)}%`;
}
