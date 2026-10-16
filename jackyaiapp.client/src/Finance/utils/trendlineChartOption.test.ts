import { describe, expect, it } from 'vitest';

import {
  buildTrendlineOption,
  chartPriceRange,
  distancePct,
  DOWN_COLOR,
  fmtPrice,
  UP_COLOR,
} from './trendlineChartOption';

import type { OhlcBar } from './trendlines';

const bars: OhlcBar[] = [
  { date: '2026-10-13', open: 10, high: 12, low: 9, close: 11 },
  { date: '2026-10-14', open: 11, high: 11.5, low: 9.5, close: 10 },
  { date: '2026-10-15', open: 10, high: 13, low: 10, close: 12.5 },
];

describe('chartPriceRange', () => {
  it('spans the lowest low to the highest high with 4% room', () => {
    const range = chartPriceRange(bars);
    expect(range.min).toBeCloseTo(9 - 0.16);
    expect(range.max).toBeCloseTo(13 + 0.16);
  });

  it('still has room when every price is the same', () => {
    const flat = chartPriceRange([{ date: '2026-10-13', open: 5, high: 5, low: 5, close: 5 }]);
    expect(flat.max).toBeGreaterThan(flat.min);
  });
});

describe('buildTrendlineOption', () => {
  const option = buildTrendlineOption(bars, { min: 8, max: 14 });
  const series = (option.series as Record<string, unknown>[])[0] as {
    data: number[][];
    itemStyle: Record<string, string>;
    markLine?: unknown;
  };

  it('feeds ECharts open, close, low, high, red up and green down', () => {
    expect(series.data[0]).toEqual([10, 11, 9, 12]);
    expect(series.itemStyle).toMatchObject({ color: UP_COLOR, color0: DOWN_COLOR });
  });

  it('fixes the price axis to the given range and leaves the lines to the overlay', () => {
    expect(option.yAxis).toMatchObject({ min: 8, max: 14 });
    expect(series.markLine).toBeUndefined();
  });
});

describe('distancePct', () => {
  it('signs the distance of the close from the line', () => {
    expect(distancePct(101, 100)).toBe('+1.0%');
    expect(distancePct(97.5, 100)).toBe('-2.5%');
  });
});

describe('fmtPrice', () => {
  it('drops the decimals of an index but keeps a stock price', () => {
    expect(fmtPrice(47180.61)).toBe('47,181');
    expect(fmtPrice(31.456)).toBe('31.46');
  });
});
