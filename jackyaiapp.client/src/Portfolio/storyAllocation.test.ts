import { describe, expect, it } from 'vitest';

import { buildStoryAllocation, STORY_MAX_ROWS_COMPACT } from './storyAllocation';

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
  it('normalises each market to 100% on its own and reports its share of the whole', () => {
    const { tw, us, fund, cashPct } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 300 }),
      makePosition({ symbol: '0050', marketValueTWD: 100 }),
      makePosition({ symbol: 'NVDA', market: 'US', marketValueTWD: 600 }),
    ]);
    expect(tw?.items.map((i) => [i.symbol, i.pct])).toEqual([
      ['2330', 75],
      ['0050', 25],
    ]);
    expect(tw?.share).toBe(40);
    expect(us?.items).toHaveLength(1);
    expect(us?.items[0].pct).toBe(100);
    expect(us?.share).toBe(60);
    expect(fund).toBeNull();
    expect(cashPct).toBeNull();
  });

  it('adds cash as one figure and to every share’s denominator', () => {
    const { tw, us, cashPct } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 300 }),
      makePosition({ symbol: 'NVDA', market: 'US', marketValueTWD: 100 }),
      makePosition({ assetType: 'CASH', symbol: 'TWD', marketValueTWD: 60 }),
      makePosition({ assetType: 'CASH', symbol: 'USD', currency: 'USD', marketValueTWD: 40 }),
    ]);
    expect(tw?.share).toBe(60);
    expect(us?.share).toBe(20);
    expect(cashPct).toBe(20);
    expect(tw?.items[0].pct).toBe(100); // holdings still sum to 100% within their market
  });

  it('lists funds as their own section, named by fund name rather than code', () => {
    const { tw, fund } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 500 }),
      makePosition({
        assetType: 'FUND',
        symbol: 'ACTI71',
        name: '安聯台灣科技',
        marketValueTWD: 300,
      }),
      makePosition({
        assetType: 'FUND',
        symbol: 'FLZ12',
        name: '富蘭克林科技',
        marketValueTWD: 200,
      }),
    ]);
    expect(tw?.share).toBe(50);
    expect(fund?.share).toBe(50);
    expect(fund?.items.map((i) => [i.symbol, i.name, i.pct])).toEqual([
      ['安聯台灣科技', '', 60],
      ['富蘭克林科技', '', 40],
    ]);
  });

  it('ignores real estate, liability and non-positive positions, and merges duplicate symbols', () => {
    const { tw, us, fund, cashPct } = buildStoryAllocation([
      makePosition({ symbol: '2330', marketValueTWD: 100 }),
      makePosition({ symbol: '2330', marketValueTWD: 100 }),
      makePosition({ symbol: '2317', marketValueTWD: 0 }),
      makePosition({ assetType: 'REAL_ESTATE', symbol: 'HOUSE', marketValueTWD: 9_999 }),
      makePosition({ assetType: 'LIABILITY', symbol: 'LOAN', marketValueTWD: -500 }),
      makePosition({ assetType: 'CASH', symbol: 'TWD', marketValueTWD: -10 }),
    ]);
    expect(tw?.items).toEqual([{ symbol: '2330', name: 'Test Co', pct: 100, isOther: false }]);
    expect(tw?.count).toBe(1);
    expect(tw?.share).toBe(100);
    expect(us).toBeNull();
    expect(fund).toBeNull();
    expect(cashPct).toBeNull();
  });

  it('folds the tail into one "其他" row once a section exceeds maxRows', () => {
    const positions = [50, 40, 30, 20, 10].map((v, i) =>
      makePosition({ symbol: `S${i}`, marketValueTWD: v }),
    );
    const { tw } = buildStoryAllocation(positions, 3);
    expect(tw?.count).toBe(5);
    expect(tw?.items.map((i) => i.symbol)).toEqual(['S0', 'S1', '其他']);
    expect(tw?.items[2]).toMatchObject({ isOther: true, name: '3 檔', pct: (60 / 150) * 100 });
  });

  it('lists fewer rows per section when three sections share the image', () => {
    const tw = [9, 8, 7, 6, 5, 4].map((v, i) =>
      makePosition({ symbol: `T${i}`, marketValueTWD: v }),
    );
    const usAndFund = [
      makePosition({ symbol: 'NVDA', market: 'US', marketValueTWD: 10 }),
      makePosition({ assetType: 'FUND', symbol: 'F1', name: 'Fund 1', marketValueTWD: 10 }),
    ];

    expect(buildStoryAllocation(tw).tw?.items).toHaveLength(6); // two sections or fewer: all 6
    const compact = buildStoryAllocation([...tw, ...usAndFund]).tw;
    expect(compact?.items).toHaveLength(STORY_MAX_ROWS_COMPACT);
    expect(compact?.items[STORY_MAX_ROWS_COMPACT - 1]).toMatchObject({
      symbol: '其他',
      name: '2 檔',
    });
  });

  it('returns no sections when there is nothing to show', () => {
    expect(buildStoryAllocation([])).toEqual({ tw: null, us: null, fund: null, cashPct: null });
  });
});
