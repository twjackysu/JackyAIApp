import { beforeEach, describe, expect, it } from 'vitest';

import {
  computeCurrentAllocation,
  computeRebalancingDeltas,
  loadRebalancingTarget,
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
  it('splits the 5 buckets as a % of liquidGrossAssetsTWD + realEstateTWD', () => {
    const summary = makeSummary({
      twStockTWD: 400_000,
      usStockTWD: 200_000,
      cashTWD: 100_000,
      fundTWD: 100_000,
      realEstateTWD: 200_000,
      liquidGrossAssetsTWD: 800_000, // twStock + usStock + cash + fund
    });
    const result = computeCurrentAllocation(summary);
    expect(result).toEqual([
      { bucket: 'twStock', currentTWD: 400_000, currentPct: 40 },
      { bucket: 'usStock', currentTWD: 200_000, currentPct: 20 },
      { bucket: 'fund', currentTWD: 100_000, currentPct: 10 },
      { bucket: 'cash', currentTWD: 100_000, currentPct: 10 },
      { bucket: 'realEstate', currentTWD: 200_000, currentPct: 20 },
    ]);
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
    const result = computeCurrentAllocation(makeSummary({}));
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
      realEstate: 0,
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
    const target: RebalancingTarget = {
      twStock: 20,
      usStock: 20,
      fund: 20,
      cash: 20,
      realEstate: 20,
    };
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
