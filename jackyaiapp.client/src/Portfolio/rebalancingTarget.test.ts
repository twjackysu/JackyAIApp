import { beforeEach, describe, expect, it } from 'vitest';

import {
  computeCurrentAllocation,
  computeRebalancingDeltas,
  loadRebalancingTarget,
  rebalancingBaseTWD,
  RebalancingTarget,
  saveRebalancingTarget,
} from './rebalancingTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

function makeSummary(overrides: Partial<PortfolioSummaryDto>): PortfolioSummaryDto {
  return {
    usdTwdRate: 32,
    totalNetWorthTWD: 0,
    twStockTWD: 0,
    usStockTWD: 0,
    cashTWD: 0,
    fundTWD: 0,
    realEstateTWD: 0,
    liabilityTWD: 0,
    liquidGrossAssetsTWD: 0,
    costBasisTWD: 0,
    unrealizedPnlTWD: 0,
    unrealizedPnlPct: 0,
    estimatedSellCostTWD: 0,
    totalDividendsTWD: 0,
    totalReturnPnlTWD: 0,
    totalReturnPct: 0,
    marketExposureTWD: 0,
    exposureOfLiquidAssetsPct: null,
    liquidExposureMultiple: null,
    drawdownBufferPct: null,
    positions: [],
    ...overrides,
  };
}

describe('computeCurrentAllocation', () => {
  it('splits the 4 liquid buckets as a % of liquidGrossAssetsTWD', () => {
    const summary = makeSummary({
      twStockTWD: 400_000,
      usStockTWD: 200_000,
      cashTWD: 100_000,
      fundTWD: 300_000,
      liquidGrossAssetsTWD: 1_000_000, // twStock + usStock + cash + fund
    });
    const result = computeCurrentAllocation(summary);
    expect(result).toEqual([
      { bucket: 'twStock', currentTWD: 400_000, currentPct: 40 },
      { bucket: 'usStock', currentTWD: 200_000, currentPct: 20 },
      { bucket: 'fund', currentTWD: 300_000, currentPct: 30 },
      { bucket: 'cash', currentTWD: 100_000, currentPct: 10 },
    ]);
  });

  it('leaves real estate out of both the buckets and the denominator', () => {
    const summary = makeSummary({
      twStockTWD: 600_000,
      cashTWD: 400_000,
      liquidGrossAssetsTWD: 1_000_000,
      realEstateTWD: 20_000_000, // a house must neither be a bucket nor shrink the % base
    });
    const result = computeCurrentAllocation(summary);
    expect(result.map((r) => r.bucket)).toEqual(['twStock', 'usStock', 'fund', 'cash']);
    expect(result.find((r) => r.bucket === 'twStock')?.currentPct).toBe(60);
    expect(result.find((r) => r.bucket === 'cash')?.currentPct).toBe(40);
    expect(rebalancingBaseTWD(summary)).toBe(1_000_000);
  });

  it('excludes liability from both the buckets and the denominator', () => {
    const summary = makeSummary({
      cashTWD: 100_000,
      liquidGrossAssetsTWD: 100_000,
      liabilityTWD: 5_000_000, // large debt must not appear as a bucket or shrink the % base
    });
    const result = computeCurrentAllocation(summary);
    expect(result.find((r) => r.bucket === 'cash')?.currentPct).toBe(100);
    expect(result.map((r) => r.bucket)).not.toContain('liability');
  });

  it('returns 0% for every bucket instead of NaN/Infinity when total assets are 0', () => {
    const result = computeCurrentAllocation(makeSummary({ realEstateTWD: 10_000_000 }));
    expect(result.every((r) => r.currentPct === 0)).toBe(true);
    expect(result.every((r) => Number.isFinite(r.currentPct))).toBe(true);
  });
});

describe('computeRebalancingDeltas', () => {
  it('is positive (buy) when target% exceeds current%, negative (sell) otherwise', () => {
    const summary = makeSummary({
      twStockTWD: 200_000,
      cashTWD: 800_000,
      liquidGrossAssetsTWD: 1_000_000,
    });
    const current = computeCurrentAllocation(summary);
    const target: RebalancingTarget = {
      twStock: 50, // up from 20% -> buy
      usStock: 0,
      fund: 0,
      cash: 50, // down from 80% -> sell
    };
    const deltas = computeRebalancingDeltas(current, target, 1_000_000);

    const tw = deltas.find((d) => d.bucket === 'twStock');
    expect(tw?.currentPct).toBe(20);
    expect(tw?.targetPct).toBe(50);
    expect(tw?.deltaTWD).toBe(300_000); // 50% of 1M (500k) - current 200k

    const cash = deltas.find((d) => d.bucket === 'cash');
    expect(cash?.deltaTWD).toBe(-300_000); // 50% of 1M (500k) - current 800k
  });

  it('handles a total of 0 without dividing by zero (delta is 0 when total assets are 0)', () => {
    const current = computeCurrentAllocation(makeSummary({}));
    const target: RebalancingTarget = { twStock: 25, usStock: 25, fund: 25, cash: 25 };
    const deltas = computeRebalancingDeltas(current, target, 0);
    expect(deltas.every((d) => d.deltaTWD === 0)).toBe(true);
  });
});

describe('loadRebalancingTarget / saveRebalancingTarget', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('round-trips through localStorage', () => {
    expect(loadRebalancingTarget()).toBeNull();
    const target: RebalancingTarget = { twStock: 40, usStock: 20, fund: 10, cash: 30 };
    saveRebalancingTarget(target);
    expect(loadRebalancingTarget()).toEqual(target);
  });

  it('converts a target saved with a real-estate bucket, keeping the liquid proportions', () => {
    // Saved before real estate left the allocation: % of liquid assets + real estate.
    localStorage.setItem(
      'portfolio.rebalancingTarget',
      JSON.stringify({ twStock: 30, usStock: 20, fund: 10, cash: 10, realEstate: 30 }),
    );

    const expected = { twStock: 42.9, usStock: 28.6, fund: 14.3, cash: 14.3 };
    expect(loadRebalancingTarget()).toEqual(expected);
    // Written back in the new shape, so it is converted once, not on every load.
    expect(JSON.parse(localStorage.getItem('portfolio.rebalancingTarget')!)).toEqual(expected);
  });

  it('keeps an old target that summed to under 100% under 100%', () => {
    localStorage.setItem(
      'portfolio.rebalancingTarget',
      JSON.stringify({ twStock: 40, usStock: 0, fund: 0, cash: 20, realEstate: 20 }),
    );
    // 60 liquid + 20 real estate = 80 in total; the liquid buckets now carry the whole 80.
    expect(loadRebalancingTarget()).toEqual({ twStock: 53.3, usStock: 0, fund: 0, cash: 26.7 });
  });

  it('starts over when the old target was all real estate', () => {
    localStorage.setItem(
      'portfolio.rebalancingTarget',
      JSON.stringify({ twStock: 0, usStock: 0, fund: 0, cash: 0, realEstate: 100 }),
    );
    expect(loadRebalancingTarget()).toBeNull();
  });
});
