import { describe, expect, it } from 'vitest';

import { buildStoryAllocation } from './storyAllocation';

import { PositionDto } from '@/apis/portfolioApis/types';

function makePosition(overrides: Partial<PositionDto>): PositionDto {
  return {
    assetType: 'STOCK',
    symbol: 'TEST',
    name: 'Test Co',
    market: 'TW',
    currency: 'TWD',
    quantity: 1,
    currentPrice: 0,
    marketValueLocal: 0,
    marketValueTWD: 0,
    exposureFactor: 1,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
    ...overrides,
  };
}

describe('buildStoryAllocation', () => {
  it('normalises each market to 100% on its own and reports its share of all stocks', () => {
    const { tw, us } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 300 }),
      makePosition({ symbol: '0050', marketValueTWD: 100 }),
      makePosition({ symbol: 'NVDA', market: 'US', marketValueTWD: 600 }),
    ]);
    expect(tw?.items.map((i) => [i.symbol, i.pct])).toEqual([
      ['2330', 75],
      ['0050', 25],
    ]);
    expect(tw?.shareOfStocks).toBe(40);
    expect(us?.items).toHaveLength(1);
    expect(us?.items[0].pct).toBe(100);
    expect(us?.shareOfStocks).toBe(60);
  });

  it('ignores non-stock and non-positive positions, and merges duplicate symbols', () => {
    const { tw, us } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 100 }),
      makePosition({ symbol: '2330', marketValueTWD: 100 }),
      makePosition({ symbol: '2317', marketValueTWD: 0 }),
      makePosition({ assetType: 'FUND', symbol: 'F1', marketValueTWD: 999 }),
      makePosition({ assetType: 'CASH', symbol: 'TWD', marketValueTWD: 999 }),
    ]);
    expect(tw?.items).toEqual([{ symbol: '2330', name: 'Test Co', pct: 100, isOther: false }]);
    expect(tw?.count).toBe(1);
    expect(us).toBeNull();
  });

  it('folds the tail into one "其他" row once a market exceeds maxRows', () => {
    const positions = [50, 40, 30, 20, 10].map((v, i) =>
      makePosition({ symbol: `S${i}`, marketValueTWD: v }),
    );
    const { tw } = buildStoryAllocation(positions, 3);
    expect(tw?.count).toBe(5);
    expect(tw?.items.map((i) => i.symbol)).toEqual(['S0', 'S1', '其他']);
    expect(tw?.items[2]).toMatchObject({ isOther: true, name: '3 檔', pct: (60 / 150) * 100 });
  });

  it('returns no sections when there are no stocks', () => {
    expect(buildStoryAllocation([])).toEqual({ tw: null, us: null });
  });
});
