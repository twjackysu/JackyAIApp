import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { readJson, writeJson } from '@/utils/storage';

const STORAGE_KEY = 'portfolio.rebalancingTarget';

export interface RebalancingTarget {
  twStock: number;
  usStock: number;
  fund: number;
  cash: number;
  realEstate: number;
}

export const BUCKETS: (keyof RebalancingTarget)[] = [
  'twStock',
  'usStock',
  'fund',
  'cash',
  'realEstate',
];

export const BUCKET_LABEL: Record<keyof RebalancingTarget, string> = {
  twStock: '台股',
  usStock: '美股',
  fund: '基金',
  cash: '現金',
  realEstate: '不動產',
};

/** A per-browser preference, not account data — same reasoning as feeDiscountRate.ts: a
 * rebalancing target is the user's own planning input, not something other users/devices need
 * to see, so it doesn't need a backend table. */
export function loadRebalancingTarget(): RebalancingTarget | null {
  return readJson<RebalancingTarget>(STORAGE_KEY);
}

export function saveRebalancingTarget(target: RebalancingTarget): void {
  writeJson(STORAGE_KEY, target);
}

export interface CurrentAllocationEntry {
  bucket: keyof RebalancingTarget;
  currentTWD: number;
  currentPct: number;
}

/** Total assets denominator for the rebalancing pie: liquid gross assets + real estate.
 * Liability is deliberately excluded — debt isn't something you allocate into, it's a
 * reduction of net worth. */
export function totalAssetsTWD(summary: PortfolioSummaryDto): number {
  return summary.liquidGrossAssetsTWD + summary.realEstateTWD;
}

/** Bucket → current TWD value and current % of total assets, straight off `PortfolioSummaryDto` —
 * no new fetch, no divide-by-zero (0% when there are no assets yet). */
export function computeCurrentAllocation(summary: PortfolioSummaryDto): CurrentAllocationEntry[] {
  const total = totalAssetsTWD(summary);
  const byBucket: Record<keyof RebalancingTarget, number> = {
    twStock: summary.twStockTWD,
    usStock: summary.usStockTWD,
    fund: summary.fundTWD,
    cash: summary.cashTWD,
    realEstate: summary.realEstateTWD,
  };
  return BUCKETS.map((bucket) => {
    const currentTWD = byBucket[bucket];
    return { bucket, currentTWD, currentPct: total > 0 ? (currentTWD / total) * 100 : 0 };
  });
}

export interface RebalancingDelta {
  bucket: keyof RebalancingTarget;
  currentPct: number;
  targetPct: number;
  deltaTWD: number;
}

/** Positive `deltaTWD` = buy more of this bucket to reach target; negative = sell/reduce. */
export function computeRebalancingDeltas(
  current: CurrentAllocationEntry[],
  target: RebalancingTarget,
  totalAssets: number,
): RebalancingDelta[] {
  return current.map(({ bucket, currentPct, currentTWD }) => {
    const targetPct = target[bucket];
    return {
      bucket,
      currentPct,
      targetPct,
      deltaTWD: (targetPct / 100) * totalAssets - currentTWD,
    };
  });
}
