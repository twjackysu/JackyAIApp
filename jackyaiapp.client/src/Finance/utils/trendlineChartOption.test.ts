import { describe, expect, it } from 'vitest';

import {
  buildTrendlineOption,
  chartPriceRange,
  distancePct,
  DOWN_COLOR,
  fmtPrice,
  fmtVolumeShort,
  trendlineChartHeight,
  trendlineTooltip,
  TW_VOLUME,
  UP_COLOR,
  US_VOLUME,
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
    expect((option.yAxis as unknown[])[0]).toMatchObject({ min: 8, max: 14 });
    expect(series.markLine).toBeUndefined();
  });

  it('has no volume pane without a unit, or without volume', () => {
    expect(option.grid).toHaveLength(1);
    expect(option.series).toHaveLength(1);
    expect(
      buildTrendlineOption(bars, { min: 8, max: 14 }, { volume: TW_VOLUME }).grid,
    ).toHaveLength(1);
  });
});

describe('the volume pane', () => {
  const traded: OhlcBar[] = [
    { ...bars[0], volume: 43_046_982 },
    { ...bars[1], volume: null },
    { ...bars[2], volume: 58_486_711 },
  ];
  const option = buildTrendlineOption(traded, { min: 8, max: 14 }, { volume: TW_VOLUME });
  const volumeSeries = (option.series as Record<string, unknown>[])[1] as {
    xAxisIndex: number;
    yAxisIndex: number;
    data: ({ value: number; itemStyle: { color: string } } | '-')[];
  };

  it('sits under the prices on its own axes, with the dates moved under it', () => {
    expect(option.grid).toHaveLength(2);
    expect(volumeSeries).toMatchObject({ xAxisIndex: 1, yAxisIndex: 1 });
    const [priceX, volumeX] = option.xAxis as { axisLabel: { show?: boolean } }[];
    expect(priceX.axisLabel.show).toBe(false);
    expect(volumeX.axisLabel.show).not.toBe(false);
    expect(trendlineChartHeight(true)).toBeGreaterThan(trendlineChartHeight(false));
  });

  it('draws each day in lots, coloured like its candle, and nothing for a day without volume', () => {
    expect(volumeSeries.data[0]).toEqual({ value: 43_046.982, itemStyle: { color: UP_COLOR } });
    expect(volumeSeries.data[1]).toBe('-');
    expect(volumeSeries.data[2]).toEqual({ value: 58_486.711, itemStyle: { color: UP_COLOR } });
    const down = buildTrendlineOption(
      [{ ...bars[1], volume: 1000 }, ...traded],
      { min: 8, max: 14 },
      {
        volume: TW_VOLUME,
      },
    );
    expect(
      (down.series as { data: { itemStyle: { color: string } }[] }[])[1].data[0].itemStyle.color,
    ).toBe(DOWN_COLOR);
  });

  it('adds the day’s volume to the tooltip', () => {
    const formatter = (option.tooltip as { formatter: (p: unknown) => string }).formatter;
    expect(formatter([{ dataIndex: 0 }])).toContain('43,047 張');
    expect(formatter([{ dataIndex: 1 }])).not.toContain('量');
  });
});

describe('fmtVolumeShort', () => {
  it('shortens to 萬 and 億', () => {
    expect(fmtVolumeShort(8_000)).toBe('8,000');
    expect(fmtVolumeShort(43_047)).toBe('4.3萬');
    expect(fmtVolumeShort(1_500_000)).toBe('150萬');
    expect(fmtVolumeShort(123_000_000)).toBe('1.2億');
  });
});

describe('trendlineTooltip', () => {
  const text = (html: string) =>
    html
      .replace(/<[^>]+>/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();

  it('shows the bar in Chinese, then each line over it, highest first', () => {
    const html = trendlineTooltip(bars[0], [
      { side: 'support', value: 9.5 },
      { side: 'resistance', value: 12.25 },
    ]);
    expect(text(html)).toBe('2026-10-13 開 10 高 12 低 9 收 11 壓力線 12.25 支撐線 9.5');
  });

  it('is just the bar on a day no line covers', () => {
    expect(text(trendlineTooltip(bars[1], []))).toBe('2026-10-14 開 11 高 11.5 低 9.5 收 10');
  });

  it('shows the volume in the market’s unit', () => {
    const day = { ...bars[1], volume: 52_345_678 };
    expect(text(trendlineTooltip(day, [], TW_VOLUME))).toContain('收 10 量 52,346 張');
    expect(text(trendlineTooltip(day, [], US_VOLUME))).toContain('量 52,345,678 股');
    expect(text(trendlineTooltip(day, []))).not.toContain('量');
  });

  it('is what the option shows for the hovered bar', () => {
    const option = buildTrendlineOption(
      bars,
      { min: 8, max: 14 },
      { linesAt: (i) => (i === 2 ? [{ side: 'support', value: 10 }] : []) },
    );
    const formatter = (option.tooltip as { formatter: (p: unknown) => string }).formatter;
    expect(text(formatter([{ dataIndex: 2 }]))).toContain('支撐線 10');
    expect(text(formatter([{ dataIndex: 1 }]))).not.toContain('線');
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
