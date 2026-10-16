import { describe, expect, it } from 'vitest';

import {
  buildTrendlineOption,
  distancePct,
  DOWN_COLOR,
  RESISTANCE_COLOR,
  SUPPORT_COLOR,
  UP_COLOR,
} from './trendlineChartOption';

import type { OhlcBar, TrendlineAnalysis } from './trendlines';

const bars: OhlcBar[] = [
  { date: '2026-10-13', open: 10, high: 12, low: 9, close: 11 },
  { date: '2026-10-14', open: 11, high: 11.5, low: 9.5, close: 10 },
  { date: '2026-10-15', open: 10, high: 13, low: 10, close: 12.5 },
];

const analysis: TrendlineAnalysis = {
  lines: [
    { side: 'support', start: 0, end: 2, slope: 0.5, intercept: 9, touches: 3, isActive: true },
    { side: 'resistance', start: 0, end: 1, slope: 0, intercept: 12, touches: 2, isActive: false },
  ],
  swings: [],
  majorSwings: [],
  support: 10,
  resistance: null,
};

describe('buildTrendlineOption', () => {
  const option = buildTrendlineOption(bars, analysis);
  const series = (option.series as Record<string, unknown>[])[0] as {
    data: number[][];
    itemStyle: Record<string, string>;
    markLine: {
      data: [
        { coord: [string, number]; lineStyle: Record<string, unknown> },
        { coord: [string, number] },
      ][];
    };
  };

  it('feeds ECharts open, close, low, high, red up and green down', () => {
    expect(series.data[0]).toEqual([10, 11, 9, 12]);
    expect(series.itemStyle).toMatchObject({ color: UP_COLOR, color0: DOWN_COLOR });
  });

  it('draws each line from its first swing point to its end, bolder while in force', () => {
    const [support, resistance] = series.markLine.data;
    expect(support[0].coord).toEqual(['2026-10-13', 9]);
    expect(support[1].coord).toEqual(['2026-10-15', 10]);
    expect(support[0].lineStyle).toMatchObject({ color: SUPPORT_COLOR, width: 2.5 });
    expect(resistance[1].coord).toEqual(['2026-10-14', 12]);
    expect(resistance[0].lineStyle).toMatchObject({ color: RESISTANCE_COLOR, width: 1.5 });
  });
});

describe('distancePct', () => {
  it('signs the distance of the close from the line', () => {
    expect(distancePct(101, 100)).toBe('+1.0%');
    expect(distancePct(97.5, 100)).toBe('-2.5%');
  });
});
