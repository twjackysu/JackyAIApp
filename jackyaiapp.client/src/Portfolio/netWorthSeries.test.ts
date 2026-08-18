import { describe, expect, it } from 'vitest';

import {
  bucketSeries,
  liquidNetWorth,
  SeriesPoint,
  toDeltas,
  toLiquidNetWorthSeries,
} from './netWorthSeries';

import { NetWorthDataPoint } from '@/apis/portfolioApis/types';

function point(date: string, groups: Record<string, number>): NetWorthDataPoint {
  const entries = Object.entries(groups);
  return {
    date,
    // Mirrors the backend invariant: totalTWD is the sum of every group, with liability already
    // stored negative. Tests that hand-computed a different total would be testing a fiction.
    totalTWD: entries.reduce((sum, [, v]) => sum + v, 0),
    groups: entries.map(([key, valueTWD]) => ({ key, valueTWD })),
  };
}

describe('liquidNetWorth', () => {
  it('subtracts only real estate — debt is already negative in totalTWD', () => {
    const p = point('2026-08-01', { Cash: 500_000, TwStock: 1_000_000, Liability: -300_000 });
    expect(p.totalTWD).toBe(1_200_000);
    expect(liquidNetWorth(p)).toBe(1_200_000);
  });

  it('removes the Property group', () => {
    const p = point('2026-08-01', {
      Cash: 500_000,
      Property: 16_000_000,
      Liability: -9_000_000,
    });
    // total = 7,500,000; liquid = total - 16,000,000 = -8,500,000 (debt exceeds liquid assets)
    expect(liquidNetWorth(p)).toBe(-8_500_000);
  });

  it('is a no-op when there is no real estate', () => {
    const p = point('2026-08-01', { Cash: 100 });
    expect(liquidNetWorth(p)).toBe(100);
  });
});

describe('bucketSeries', () => {
  const daily: SeriesPoint[] = [
    { date: '2026-08-03', value: 10 }, // Monday
    { date: '2026-08-05', value: 20 },
    { date: '2026-08-07', value: 30 }, // still week of 08-03
    { date: '2026-08-10', value: 40 }, // Monday, next week
    { date: '2026-08-11', value: 50 },
  ];

  it('daily passes everything through unchanged', () => {
    expect(bucketSeries(daily, 'daily')).toEqual(daily);
  });

  it('weekly takes each week last value — net worth is a stock, not a flow', () => {
    expect(bucketSeries(daily, 'weekly')).toEqual([
      { date: '2026-08-07', value: 30 },
      { date: '2026-08-11', value: 50 },
    ]);
  });

  it('monthly takes each month last value', () => {
    const spanning: SeriesPoint[] = [
      { date: '2026-07-30', value: 1 },
      { date: '2026-07-31', value: 2 },
      { date: '2026-08-01', value: 3 },
    ];
    expect(bucketSeries(spanning, 'monthly')).toEqual([
      { date: '2026-07-31', value: 2 },
      { date: '2026-08-01', value: 3 },
    ]);
  });

  it('groups a week by its Monday regardless of which weekday the range starts on', () => {
    // Starting mid-week must not create a bucket boundary that drifts.
    const midweekStart: SeriesPoint[] = [
      { date: '2026-08-05', value: 1 }, // Wed, week of 08-03
      { date: '2026-08-07', value: 2 }, // Fri, same week
      { date: '2026-08-10', value: 3 }, // Mon, next week
    ];
    expect(bucketSeries(midweekStart, 'weekly')).toEqual([
      { date: '2026-08-07', value: 2 },
      { date: '2026-08-10', value: 3 },
    ]);
  });

  it('handles empty and single-point input', () => {
    expect(bucketSeries([], 'weekly')).toEqual([]);
    expect(bucketSeries([{ date: '2026-08-05', value: 7 }], 'monthly')).toEqual([
      { date: '2026-08-05', value: 7 },
    ]);
  });
});

describe('toDeltas', () => {
  it('nulls the first delta rather than zeroing it', () => {
    const result = toDeltas([
      { date: 'a', value: 100 },
      { date: 'b', value: 130 },
      { date: 'c', value: 120 },
    ]);
    expect(result).toEqual([
      { date: 'a', delta: null },
      { date: 'b', delta: 30 },
      { date: 'c', delta: -10 },
    ]);
  });

  it('reports a genuine no-change period as 0, distinct from the unknown first period', () => {
    const result = toDeltas([
      { date: 'a', value: 100 },
      { date: 'b', value: 100 },
    ]);
    expect(result[0].delta).toBeNull();
    expect(result[1].delta).toBe(0);
  });

  it('handles empty and single-point input', () => {
    expect(toDeltas([])).toEqual([]);
    expect(toDeltas([{ date: 'a', value: 5 }])).toEqual([{ date: 'a', delta: null }]);
  });
});

describe('toLiquidNetWorthSeries', () => {
  it('surfaces a small change that the absolute scale would hide — the whole point of the delta chart', () => {
    // Assets ~10M, debt ~8M: the classic case where +20k/-10k is invisible on a 0..10M axis.
    const points = [
      point('2026-08-03', { Cash: 10_000_000, Liability: -8_000_000 }),
      point('2026-08-10', { Cash: 10_020_000, Liability: -7_990_000 }),
    ];
    const series = toLiquidNetWorthSeries(points, 'weekly');
    expect(series.map((s) => s.value)).toEqual([2_000_000, 2_030_000]);

    const deltas = toDeltas(series);
    expect(deltas[0].delta).toBeNull();
    // +20k of assets and +10k of debt paydown = +30k of net worth, on its own axis.
    expect(deltas[1].delta).toBe(30_000);
  });

  it('excludes real estate throughout the pipeline', () => {
    const points = [
      point('2026-08-03', { Cash: 1_000_000, Property: 16_000_000 }),
      point('2026-08-04', { Cash: 1_100_000, Property: 16_000_000 }),
    ];
    const series = toLiquidNetWorthSeries(points, 'daily');
    expect(series.map((s) => s.value)).toEqual([1_000_000, 1_100_000]);
    // A static 16M house must not swamp a 100k change.
    expect(toDeltas(series)[1].delta).toBe(100_000);
  });
});
