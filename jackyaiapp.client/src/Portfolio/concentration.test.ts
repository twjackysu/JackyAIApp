import { describe, expect, it } from 'vitest';

import { computeConcentration, concentrationLevel } from './concentration';

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

describe('concentrationLevel', () => {
  it('bands at the standard HHI thresholds (1500 / 2500 on a 0-10000 scale)', () => {
    expect(concentrationLevel(1499).label).toBe('分散');
    expect(concentrationLevel(1500).label).toBe('中度集中');
    expect(concentrationLevel(2499).label).toBe('中度集中');
    expect(concentrationLevel(2500).label).toBe('高度集中');
  });
});

describe('computeConcentration', () => {
  it('returns null when there are no priced STOCK/FUND holdings', () => {
    expect(computeConcentration([])).toBeNull();
    expect(
      computeConcentration([makePosition({ assetType: 'CASH', marketValueTWD: 100_000 })]),
    ).toBeNull();
    expect(
      computeConcentration([makePosition({ assetType: 'STOCK', marketValueTWD: 0 })]),
    ).toBeNull();
  });

  it('excludes CASH/LIABILITY/REAL_ESTATE from the concentration universe', () => {
    const result = computeConcentration([
      makePosition({ assetType: 'STOCK', symbol: 'A', marketValueTWD: 100_000 }),
      makePosition({ assetType: 'CASH', marketValueTWD: 1_000_000 }),
      makePosition({ assetType: 'REAL_ESTATE', marketValueTWD: 5_000_000 }),
    ]);
    expect(result?.sorted).toHaveLength(1);
    expect(result?.top1Pct).toBe(100); // the one STOCK position is 100% of the STOCK/FUND universe
  });

  it('computes a perfectly diversified 4-way split at HHI = 2500 exactly', () => {
    // Four equal 25% positions: HHI = 4 * 25^2 = 2500 (the high-concentration boundary itself).
    const result = computeConcentration(
      ['A', 'B', 'C', 'D'].map((symbol) =>
        makePosition({ assetType: 'STOCK', symbol, marketValueTWD: 250_000 }),
      ),
    );
    expect(result?.hhi).toBe(2500);
    expect(result?.top1Pct).toBe(25);
    expect(result?.top3Pct).toBe(75);
    expect(result?.top5Pct).toBe(100); // only 4 positions exist; slice(0,5) still sums all of them
  });

  it('a single 100% concentrated position hits the maximum HHI of 10000', () => {
    const result = computeConcentration([
      makePosition({ assetType: 'FUND', symbol: 'ONLY', marketValueTWD: 1_000_000 }),
    ]);
    expect(result?.hhi).toBe(10000);
    expect(result?.level.label).toBe('高度集中');
  });

  it('sorts holdings by market value descending, largest first', () => {
    const result = computeConcentration([
      makePosition({ assetType: 'STOCK', symbol: 'SMALL', marketValueTWD: 10_000 }),
      makePosition({ assetType: 'STOCK', symbol: 'BIG', marketValueTWD: 90_000 }),
    ]);
    expect(result?.sorted.map((p) => p.symbol)).toEqual(['BIG', 'SMALL']);
    expect(result?.shares).toEqual([90, 10]);
  });
});
