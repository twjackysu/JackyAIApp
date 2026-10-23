import { describe, expect, it } from 'vitest';

import { breakdownOf, pointsForView, queryGroupByOf } from './breakdown';
import { seriesColor, seriesLabel } from './palette';

import type { NetWorthDataPoint } from '@/apis/portfolioApis/types';

const day = (date: string, groups: Record<string, number>): NetWorthDataPoint => ({
  date,
  totalTWD: 0,
  groups: Object.entries(groups).map(([key, valueTWD]) => ({ key, valueTWD })),
});

describe('queryGroupByOf', () => {
  it('動產/不動產 asks for asset types; every other view for itself', () => {
    expect(queryGroupByOf('liquidity')).toBe('assetType');
    expect(queryGroupByOf('holding')).toBe('holding');
  });
});

describe('pointsForView', () => {
  it('動產/不動產 folds every group but real estate and debt into 動產, and drops an empty fold', () => {
    const [folded] = pointsForView(
      [day('2026-10-01', { TwStock: 100, Fund: 50, Property: 300, Liability: -80 })],
      'liquidity',
    );
    expect(folded.groups).toEqual([
      { key: 'Liquid', valueTWD: 150 },
      { key: 'Property', valueTWD: 300 },
      { key: 'Liability', valueTWD: -80 },
    ]);
    expect(pointsForView([day('2026-10-01', { Cash: 10 })], 'liquidity')[0].groups).toEqual([
      { key: 'Liquid', valueTWD: 10 },
    ]);
  });

  it('any other view shows the days as they came', () => {
    const points = [day('2026-10-01', { TwStock: 100 })];
    expect(pointsForView(points, 'assetType')).toBe(points);
  });
});

describe('breakdownOf', () => {
  it('a series per key seen in the range, 0 on a day it is missing, none for one zero throughout', () => {
    const series = breakdownOf([
      day('2026-10-01', { Cash: 10, Fund: 0 }),
      day('2026-10-02', { Cash: 20, AI伺服器: 5, Fund: 0 }),
    ]);
    expect(series.map((s) => [s.key, s.name, s.values])).toEqual([
      ['Cash', '現金', [10, 20]],
      ['AI伺服器', 'AI伺服器', [0, 5]],
    ]);
    expect(series[0].color).toBe(seriesColor('Cash'));
  });
});

describe('palette', () => {
  it('a known key has its label and colour; another keeps its key and gets one from its name', () => {
    expect([seriesLabel('Liability'), seriesColor('Liability')]).toEqual(['負債', '#ef4444']);
    expect(seriesLabel('半導體設備')).toBe('半導體設備');
    expect(seriesColor('半導體設備')).toBe(seriesColor('半導體設備'));
    expect(seriesColor('半導體設備')).not.toBe(seriesColor('AI伺服器'));
  });
});
