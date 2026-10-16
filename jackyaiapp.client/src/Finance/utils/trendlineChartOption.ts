import type { OhlcBar } from './trendlines';
import type { CandlestickChartOption } from '../charts/registerFinanceCharts';

/** Taiwan convention: red up, green down. */
export const UP_COLOR = '#EF5350';
export const DOWN_COLOR = '#26A69A';
export const SUPPORT_COLOR = '#42A5F5';
export const RESISTANCE_COLOR = '#BA68C8';
/** A line being drawn, before it is a support or a resistance. */
export const DRAFT_COLOR = '#FFB74D';

export const trendlineColor = (side: 'support' | 'resistance') =>
  side === 'support' ? SUPPORT_COLOR : RESISTANCE_COLOR;

export interface PriceRange {
  min: number;
  max: number;
}

/**
 * The price axis: the lowest low to the highest high, with a little room. It is fixed rather than
 * left to ECharts so the overlay that draws the trendlines knows the exact scale, and so moving a
 * line never rescales the chart under the user's finger.
 */
export function chartPriceRange(bars: OhlcBar[]): PriceRange {
  const low = Math.min(...bars.map((b) => b.low));
  const high = Math.max(...bars.map((b) => b.high));
  const pad = (high - low || high || 1) * 0.04;
  return { min: low - pad, max: high + pad };
}

/**
 * Candlesticks only. The trendlines are drawn over the chart by TrendlineOverlay, which the user
 * can drag; ECharts' mark lines can't be dragged.
 */
export function buildTrendlineOption(bars: OhlcBar[], range: PriceRange): CandlestickChartOption {
  return {
    animation: false,
    grid: { left: 8, right: 64, top: 16, bottom: 28 },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' },
      valueFormatter: (v) => (typeof v === 'number' ? v.toLocaleString('zh-TW') : String(v)),
    },
    xAxis: {
      type: 'category',
      data: bars.map((b) => b.date),
      boundaryGap: true,
      axisLabel: { hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      min: range.min,
      max: range.max,
      position: 'right',
      splitNumber: 6,
      // The ends are the padded range, not round numbers; the ticks in between are.
      axisLabel: { showMinLabel: false, showMaxLabel: false },
    },
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
      },
    ],
  };
}

/** Where the last close sits against a line's value today, e.g. "+1.2%". */
export function distancePct(lastClose: number, level: number): string {
  const pct = (lastClose / level - 1) * 100;
  return `${pct >= 0 ? '+' : ''}${pct.toFixed(1)}%`;
}

/** A price as the chart labels it: whole numbers for an index, two decimals for a stock. */
export const fmtPrice = (price: number) =>
  price.toLocaleString('zh-TW', { maximumFractionDigits: Math.abs(price) >= 1000 ? 0 : 2 });
