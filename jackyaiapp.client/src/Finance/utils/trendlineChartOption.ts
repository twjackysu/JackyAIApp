import type { OhlcBar, TrendlineSide } from './trendlines';
import type { CandlestickChartOption } from '../charts/registerFinanceCharts';

/** Taiwan convention: red up, green down. */
export const UP_COLOR = '#EF5350';
export const DOWN_COLOR = '#26A69A';
export const SUPPORT_COLOR = '#42A5F5';
export const RESISTANCE_COLOR = '#BA68C8';

export const trendlineColor = (side: TrendlineSide) =>
  side === 'support' ? SUPPORT_COLOR : RESISTANCE_COLOR;

export const trendlineName = (side: TrendlineSide) => (side === 'support' ? '支撐線' : '壓力線');

/** A trendline's value on one day. */
export interface LineValue {
  side: TrendlineSide;
  value: number;
}

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

const tooltipRow = (label: string, value: number, marker = '') =>
  `<div style="display:flex;justify-content:space-between;gap:16px">` +
  `<span>${marker}${label}</span><b>${fmtPrice(value)}</b></div>`;

const lineMarker = (side: TrendlineSide) =>
  `<span style="display:inline-block;width:12px;height:3px;border-radius:2px;margin:0 6px 3px 0;` +
  `background:${trendlineColor(side)}"></span>`;

/** The tooltip for one bar: its prices, then each trendline drawn over that day, highest first. */
export function trendlineTooltip(bar: OhlcBar, lines: LineValue[]): string {
  return [
    `<div style="font-weight:600;margin-bottom:4px">${bar.date}</div>`,
    tooltipRow('開', bar.open),
    tooltipRow('高', bar.high),
    tooltipRow('低', bar.low),
    tooltipRow('收', bar.close),
    ...(lines.length > 0 ? ['<div style="margin-top:6px"></div>'] : []),
    ...[...lines]
      .sort((p, q) => q.value - p.value)
      .map((l) => tooltipRow(trendlineName(l.side), l.value, lineMarker(l.side))),
  ].join('');
}

/**
 * Candlesticks only. The trendlines are drawn over the chart by TrendlineOverlay, which the user
 * can drag; ECharts' mark lines can't be dragged. The tooltip still lists them: `linesAt` gives the
 * lines drawn over a bar with their values there.
 */
export function buildTrendlineOption(
  bars: OhlcBar[],
  range: PriceRange,
  linesAt: (index: number) => LineValue[] = () => [],
): CandlestickChartOption {
  return {
    animation: false,
    grid: { left: 8, right: 64, top: 16, bottom: 28 },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' },
      formatter: (params) => {
        const index = (Array.isArray(params) ? params[0] : params)?.dataIndex;
        return index == null || !bars[index] ? '' : trendlineTooltip(bars[index], linesAt(index));
      },
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
