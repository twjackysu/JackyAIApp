import { describe, expect, it } from 'vitest';

import {
  barCountsByGranularity,
  bucketSeries,
  liquidNetWorth,
  MIN_USEFUL_BARS,
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

/** A raw daily point, where open and close are the same thing (bucketing derives the real open). */
const sp = (date: string, value: number): SeriesPoint => ({ date, value, open: value });

describe('liquidNetWorth', () => {
  it('subtracts only real estate — debt is already negative in totalTWD', () => {
    const p = point('2026-08-01', { Cash: 500_000, TwStock: 1_000_000, Liability: -300_000 });
    expect(p.totalTWD).toBe(1_200_000);
    expect(liquidNetWorth(p)).toBe(1_200_000);
  });

  it('removes the Property group', () => {
    const p = point('2026-08-01', { Cash: 500_000, Property: 16_000_000, Liability: -9_000_000 });
    expect(liquidNetWorth(p)).toBe(-8_500_000);
  });

  it('is a no-op when there is no real estate', () => {
    expect(liquidNetWorth(point('2026-08-01', { Cash: 100 }))).toBe(100);
  });
});

describe('bucketSeries', () => {
  const daily: SeriesPoint[] = [
    sp('2026-08-03', 10), // Monday
    sp('2026-08-05', 20),
    sp('2026-08-07', 30), // still week of 08-03
    sp('2026-08-10', 40), // Monday, next week
    sp('2026-08-11', 50),
  ];

  it('daily passes each point through, open === close', () => {
    expect(bucketSeries(daily, 'daily')).toEqual(daily);
  });

  it('weekly closes on the last value and opens on the first value of that week', () => {
    expect(bucketSeries(daily, 'weekly')).toEqual([
      { date: '2026-08-07', value: 30, open: 10 },
      { date: '2026-08-11', value: 50, open: 40 },
    ]);
  });

  it('monthly closes on the last value of each month', () => {
    const spanning = [sp('2026-07-30', 1), sp('2026-07-31', 2), sp('2026-08-01', 3)];
    expect(bucketSeries(spanning, 'monthly')).toEqual([
      { date: '2026-07-31', value: 2, open: 1 },
      { date: '2026-08-01', value: 3, open: 3 },
    ]);
  });

  it('groups a week by its Monday regardless of which weekday the range starts on', () => {
    const midweekStart = [sp('2026-08-05', 1), sp('2026-08-07', 2), sp('2026-08-10', 3)];
    expect(bucketSeries(midweekStart, 'weekly').map((b) => b.date)).toEqual([
      '2026-08-07',
      '2026-08-10',
    ]);
  });

  it('handles empty and single-point input', () => {
    expect(bucketSeries([], 'weekly')).toEqual([]);
    expect(bucketSeries([sp('2026-08-05', 7)], 'monthly')).toEqual([
      { date: '2026-08-05', value: 7, open: 7 },
    ]);
  });
});

describe('toDeltas', () => {
  it('gives the first bucket a bar too, measured from its own open', () => {
    // Regression: the first bucket used to be dropped. An account with only two months of history
    // then produced a single bar where two were expected.
    const result = toDeltas([
      { date: '2026-07-31', value: 130, open: 100 },
      { date: '2026-08-20', value: 120, open: 131 },
    ]);
    expect(result).toEqual([
      { date: '2026-07-31', delta: 30 }, // 130 − its own open (100)
      { date: '2026-08-20', delta: -10 }, // 120 − July's close (130)
    ]);
  });

  it('bars reconcile to total change from first open to last close', () => {
    const series: SeriesPoint[] = [
      { date: 'a', value: 130, open: 100 },
      { date: 'b', value: 160, open: 131 },
      { date: 'c', value: 150, open: 161 },
    ];
    const total = series[2].value - series[0].open;
    expect(toDeltas(series).reduce((s, d) => s + d.delta, 0)).toBe(total);
  });

  it('reports a genuine no-change period as 0', () => {
    const result = toDeltas([
      { date: 'a', value: 100, open: 100 },
      { date: 'b', value: 100, open: 100 },
    ]);
    expect(result[0].delta).toBe(0);
    expect(result[1].delta).toBe(0);
  });

  it('handles empty and single-bucket input', () => {
    expect(toDeltas([])).toEqual([]);
    expect(toDeltas([{ date: 'a', value: 120, open: 100 }])).toEqual([{ date: 'a', delta: 20 }]);
  });
});

describe('toLiquidNetWorthSeries', () => {
  it('surfaces a small change that the absolute scale would hide', () => {
    const points = [
      point('2026-08-03', { Cash: 10_000_000, Liability: -8_000_000 }),
      point('2026-08-10', { Cash: 10_020_000, Liability: -7_990_000 }),
    ];
    const series = toLiquidNetWorthSeries(points, 'weekly');
    expect(series.map((s) => s.value)).toEqual([2_000_000, 2_030_000]);
    expect(toDeltas(series)[1].delta).toBe(30_000);
  });

  it('excludes real estate throughout the pipeline', () => {
    const points = [
      point('2026-08-03', { Cash: 1_000_000, Property: 16_000_000 }),
      point('2026-08-04', { Cash: 1_100_000, Property: 16_000_000 }),
    ];
    const series = toLiquidNetWorthSeries(points, 'daily');
    expect(series.map((s) => s.value)).toEqual([1_000_000, 1_100_000]);
    expect(toDeltas(series)[1].delta).toBe(100_000);
  });
});

describe('barCountsByGranularity', () => {
  function dailyPoints(fromIso: string, toIso: string): NetWorthDataPoint[] {
    const out: NetWorthDataPoint[] = [];
    const d = new Date(`${fromIso}T00:00:00Z`);
    const end = new Date(`${toIso}T00:00:00Z`);
    while (d <= end) {
      if (d.getUTCDay() !== 0 && d.getUTCDay() !== 6) {
        out.push(point(d.toISOString().slice(0, 10), { Cash: 1_000_000 + out.length * 1000 }));
      }
      d.setUTCDate(d.getUTCDate() + 1);
    }
    return out;
  }

  it('two months of history yields two monthly bars, not one', () => {
    // The reported case: an account tracked from 2026-07-01 expects a July bar and an August bar.
    const counts = barCountsByGranularity(dailyPoints('2026-07-01', '2026-08-20'));
    expect(counts.monthly).toBe(2);
    expect(counts.monthly).toBeGreaterThanOrEqual(MIN_USEFUL_BARS); // so 月 stays selectable
  });

  it('a single calendar month still yields only one monthly bar, which stays disabled', () => {
    const counts = barCountsByGranularity(dailyPoints('2026-08-03', '2026-08-20'));
    expect(counts.monthly).toBe(1);
    expect(counts.monthly).toBeLessThan(MIN_USEFUL_BARS);
  });

  it('a full 180-day window yields a bar per calendar month touched', () => {
    const counts = barCountsByGranularity(dailyPoints('2026-02-21', '2026-08-20'));
    expect(counts.monthly).toBe(7); // Feb(partial) .. Aug
  });

  it('returns zeros for empty input rather than throwing', () => {
    expect(barCountsByGranularity([])).toEqual({ daily: 0, weekly: 0, monthly: 0 });
  });
});
