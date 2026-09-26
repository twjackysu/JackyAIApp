import { beforeEach, describe, expect, it } from 'vitest';

import {
  computeCurrentAllocation,
  computeRebalancingDeltas,
  loadRebalancingTarget,
  RebalancingTarget,
  saveRebalancingTarget,
  totalAssetsTWD,
} from './rebalancingTarget';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';

function makeSummary(overrides: Partial<PortfolioSummaryDto> = {}): PortfolioSummaryDto {
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
  } as PortfolioSummaryDto;
}

describe('totalAssetsTWD / computeCurrentAllocation', () => {
  it('sums liquid gross assets + real estate as the denominator, excluding liability', () => {
    const summary = makeSummary({
      twStockTWD: 400_000,
      usStockTWD: 200_000,
      fundTWD: 100_000,
      cashTWD: 300_000,
      realEstateTWD: 5_000_000,
      liquidGrossAssetsTWD: 1_000_000,
      liabilityTWD: 2_000_000,
    });

    expect(totalAssetsTWD(summary)).toBe(6_000_000);

    const allocation = computeCurrentAllocation(summary);
    expect(allocation.map((a) => a.bucket)).toEqual([
      'twStock',
      'usStock',
      'fund',
      'cash',
      'realEstate',
    ]);
    expect(allocation.find((a) => a.bucket === 'twStock')?.currentPct).toBeCloseTo(
      (400_000 / 6_000_000) * 100,
    );
    expect(allocation.find((a) => a.bucket === 'realEstate')?.currentPct).toBeCloseTo(
      (5_000_000 / 6_000_000) * 100,
    );
    // Liability never appears as a bucket.
    expect(allocation.map((a) => a.bucket)).not.toContain('liability');
  });

  it('returns 0% for every bucket (not NaN) when total assets are 0', () => {
    const allocation = computeCurrentAllocation(makeSummary());
    expect(allocation.every((a) => a.currentPct === 0)).toBe(true);
  });
});

describe('computeRebalancingDeltas', () => {
  it('is positive (buy) when target % exceeds current %, negative (sell) otherwise', () => {
    const summary = makeSummary({
      twStockTWD: 200_000,
      cashTWD: 800_000,
      liquidGrossAssetsTWD: 1_000_000,
    });
    const current = computeCurrentAllocation(summary);
    const target: RebalancingTarget = { twStock: 50, usStock: 0, fund: 0, cash: 50, realEstate: 0 };
    const total = totalAssetsTWD(summary);

    const deltas = computeRebalancingDeltas(current, target, total);

    const twStock = deltas.find((d) => d.bucket === 'twStock');
    expect(twStock?.deltaTWD).toBeCloseTo(500_000 - 200_000); // buy 300,000 more

    const cash = deltas.find((d) => d.bucket === 'cash');
    expect(cash?.deltaTWD).toBeCloseTo(500_000 - 800_000); // sell 300,000
  });

  it('does not require target percentages to sum to 100', () => {
    const summary = makeSummary({ cashTWD: 100_000, liquidGrossAssetsTWD: 100_000 });
    const current = computeCurrentAllocation(summary);
    const target: RebalancingTarget = { twStock: 0, usStock: 0, fund: 0, cash: 10, realEstate: 0 };
    const total = totalAssetsTWD(summary);

    const deltas = computeRebalancingDeltas(current, target, total);
    const cash = deltas.find((d) => d.bucket === 'cash');
    expect(cash?.deltaTWD).toBeCloseTo(10_000 - 100_000);
  });
});

describe('loadRebalancingTarget / saveRebalancingTarget', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('round-trips a saved target through localStorage', () => {
    expect(loadRebalancingTarget()).toBeNull();

    const target: RebalancingTarget = {
      twStock: 40,
      usStock: 20,
      fund: 10,
      cash: 20,
      realEstate: 10,
    };
    saveRebalancingTarget(target);

    expect(loadRebalancingTarget()).toEqual(target);
  });
});
