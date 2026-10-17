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

/** How a market counts volume: the bars carry shares, shown in lots of `shares`. */
export interface VolumeUnit {
  label: string;
  shares: number;
}

/** Taiwan trades in 張 of 1,000 shares. */
export const TW_VOLUME: VolumeUnit = { label: '張', shares: 1000 };
/** US volume is quoted in shares. */
export const US_VOLUME: VolumeUnit = { label: '股', shares: 1 };

const GRID_SIDES = { left: 8, right: 64 };
const PRICE_TOP = 16;
const PRICE_HEIGHT_WITH_VOLUME = 300;
const VOLUME_GAP = 14;
const VOLUME_HEIGHT = 84;
/** Room under the last pane for the date labels. */
const DATE_AXIS = 28;

/** The chart's height in pixels: taller when the volume pane sits under the prices. */
export const trendlineChartHeight = (withVolume: boolean) =>
  withVolume ? PRICE_TOP + PRICE_HEIGHT_WITH_VOLUME + VOLUME_GAP + VOLUME_HEIGHT + DATE_AXIS : 360;

/** Whether these bars can show a volume pane in `unit`. */
export const hasVolume = (bars: OhlcBar[], unit: VolumeUnit | undefined): unit is VolumeUnit =>
  unit !== undefined && bars.some((b) => (b.volume ?? 0) > 0);

/** A volume in lots, short: 4.3萬, 1.2億. */
export function fmtVolumeShort(lots: number): string {
  const short = (n: number, unit: string) => `${Number(n.toFixed(n >= 100 ? 0 : 1))}${unit}`;
  if (lots >= 1e8) return short(lots / 1e8, '億');
  if (lots >= 1e4) return short(lots / 1e4, '萬');
  return Math.round(lots).toLocaleString('zh-TW');
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

const tooltipRow = (label: string, value: string, marker = '') =>
  `<div style="display:flex;justify-content:space-between;gap:16px">` +
  `<span>${marker}${label}</span><b>${value}</b></div>`;

const lineMarker = (side: TrendlineSide) =>
  `<span style="display:inline-block;width:12px;height:3px;border-radius:2px;margin:0 6px 3px 0;` +
  `background:${trendlineColor(side)}"></span>`;

/**
 * The tooltip for one bar: its prices and, given a unit, its volume; then each trendline drawn over
 * that day, highest first.
 */
export function trendlineTooltip(bar: OhlcBar, lines: LineValue[], volume?: VolumeUnit): string {
  const lots = volume && bar.volume != null ? Math.round(bar.volume / volume.shares) : null;
  return [
    `<div style="font-weight:600;margin-bottom:4px">${bar.date}</div>`,
    tooltipRow('開', fmtPrice(bar.open)),
    tooltipRow('高', fmtPrice(bar.high)),
    tooltipRow('低', fmtPrice(bar.low)),
    tooltipRow('收', fmtPrice(bar.close)),
    ...(lots != null && volume
      ? [tooltipRow('量', `${lots.toLocaleString('zh-TW')} ${volume.label}`)]
      : []),
    ...(lines.length > 0 ? ['<div style="margin-top:6px"></div>'] : []),
    ...[...lines]
      .sort((p, q) => q.value - p.value)
      .map((l) => tooltipRow(trendlineName(l.side), fmtPrice(l.value), lineMarker(l.side))),
  ].join('');
}

export interface TrendlineOptionExtras {
  /** The lines drawn over a bar, with their values there (they live in the overlay, not here). */
  linesAt?: (index: number) => LineValue[];
  /** Show a volume pane under the prices, counted in this unit. */
  volume?: VolumeUnit;
}

/**
 * Candlesticks, and under them a volume pane when there is volume to show. The trendlines are drawn
 * over the price pane by TrendlineOverlay, which the user can drag; ECharts' mark lines can't be
 * dragged. The tooltip still lists them, through `linesAt`.
 *
 * Grids and axes are always arrays with the price pane first: the overlay measures axis 0 either way.
 */
export function buildTrendlineOption(
  bars: OhlcBar[],
  range: PriceRange,
  { linesAt = () => [], volume }: TrendlineOptionExtras = {},
): CandlestickChartOption {
  const dates = bars.map((b) => b.date);
  const unit = hasVolume(bars, volume) ? volume : undefined;

  return {
    animation: false,
    grid: unit
      ? [
          { ...GRID_SIDES, top: PRICE_TOP, height: PRICE_HEIGHT_WITH_VOLUME },
          {
            ...GRID_SIDES,
            top: PRICE_TOP + PRICE_HEIGHT_WITH_VOLUME + VOLUME_GAP,
            height: VOLUME_HEIGHT,
          },
        ]
      : [{ ...GRID_SIDES, top: PRICE_TOP, bottom: DATE_AXIS }],
    // One crosshair across both panes.
    axisPointer: { link: [{ xAxisIndex: 'all' }] },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' },
      formatter: (params) => {
        const index = (Array.isArray(params) ? params[0] : params)?.dataIndex;
        return index == null || !bars[index]
          ? ''
          : trendlineTooltip(bars[index], linesAt(index), unit);
      },
    },
    xAxis: [
      {
        type: 'category',
        data: dates,
        boundaryGap: true,
        // With a volume pane the dates go under it instead, crosshair label included.
        axisLabel: { show: !unit, hideOverlap: true },
        axisTick: { show: !unit },
        axisPointer: { label: { show: !unit } },
      },
      ...(unit
        ? [
            {
              type: 'category' as const,
              gridIndex: 1,
              data: dates,
              boundaryGap: true,
              axisLabel: { hideOverlap: true },
            },
          ]
        : []),
    ],
    yAxis: [
      {
        type: 'value',
        min: range.min,
        max: range.max,
        position: 'right',
        splitNumber: 6,
        // The ends are the padded range, not round numbers; the ticks in between are.
        axisLabel: { showMinLabel: false, showMaxLabel: false },
      },
      ...(unit
        ? [
            {
              type: 'value' as const,
              gridIndex: 1,
              position: 'right' as const,
              splitNumber: 2,
              splitLine: { show: false },
              axisLabel: { formatter: (v: number) => fmtVolumeShort(v) },
              axisPointer: {
                label: {
                  formatter: ({ value }: { value: unknown }) => fmtVolumeShort(Number(value)),
                },
              },
            },
          ]
        : []),
    ],
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
      ...(unit
        ? [
            {
              type: 'bar' as const,
              name: '成交量',
              xAxisIndex: 1,
              yAxisIndex: 1,
              // In lots, coloured like the day's candle; a day without volume is left empty.
              data: bars.map((b) =>
                b.volume == null
                  ? '-'
                  : {
                      value: b.volume / unit.shares,
                      itemStyle: { color: b.close >= b.open ? UP_COLOR : DOWN_COLOR },
                    },
              ),
            },
          ]
        : []),
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
