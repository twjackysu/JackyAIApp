import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { readJson, writeJson } from '@/utils/storage';

const STORAGE_KEY = 'portfolio.rebalancingTarget';

/** Target allocation, one percentage per bucket (0-100). Independent numbers, not required to
 * sum to 100 — a user deliberately targeting less than 100% (e.g. "raise cash, haven't decided
 * the rest yet") is a legitimate target, not an error. */
export interface RebalancingTarget {
  twStock: number;
  usStock: number;
  fund: number;
  cash: number;
  realEstate: number;
}

export type RebalancingBucket = keyof RebalancingTarget;

/** A per-browser preference, not account data — kept in localStorage rather than the backend,
 * same reasoning as `feeDiscountRate.ts`: this is the user's own planning input, not something
 * other users or devices need to see, and not worth a DB table for one small object. */
export function loadRebalancingTarget(): RebalancingTarget | null {
  return readJson<RebalancingTarget>(STORAGE_KEY);
}

export function saveRebalancingTarget(target: RebalancingTarget): void {
  writeJson(STORAGE_KEY, target);
}

/** Bucket → the PortfolioSummaryDto field backing it. Real estate counts toward the allocation
 * pie (it's a real gross asset) but liability is deliberately excluded — debt isn't something
 * you allocate into, it's a reduction of net worth. */
function bucketTWD(summary: PortfolioSummaryDto, bucket: RebalancingBucket): number {
  switch (bucket) {
    case 'twStock':
      return summary.twStockTWD;
    case 'usStock':
      return summary.usStockTWD;
    case 'fund':
      return summary.fundTWD;
    case 'cash':
      return summary.cashTWD;
    case 'realEstate':
      return summary.realEstateTWD;
  }
}

export const REBALANCING_BUCKETS: RebalancingBucket[] = [
  'twStock',
  'usStock',
  'fund',
  'cash',
  'realEstate',
];

export interface CurrentAllocationEntry {
  bucket: RebalancingBucket;
  currentTWD: number;
  currentPct: number;
}

/** Current allocation across the 5 buckets, from `PortfolioSummaryDto` alone. The denominator is
 * gross assets (`liquidGrossAssetsTWD + realEstateTWD`) — NOT net of liability, matching how
 * `PortfolioSummaryDto`'s own comments already frame `liquidGrossAssetsTWD`. */
export function computeCurrentAllocation(summary: PortfolioSummaryDto): CurrentAllocationEntry[] {
  const totalAssetsTWD = summary.liquidGrossAssetsTWD + summary.realEstateTWD;
  return REBALANCING_BUCKETS.map((bucket) => {
    const currentTWD = bucketTWD(summary, bucket);
    return {
      bucket,
      currentTWD,
      currentPct: totalAssetsTWD > 0 ? (currentTWD / totalAssetsTWD) * 100 : 0,
    };
  });
}

export interface RebalancingDelta {
  bucket: RebalancingBucket;
  currentPct: number;
  targetPct: number;
  deltaTWD: number;
}

/** Positive deltaTWD = buy more of this bucket, negative = sell/reduce. */
export function computeRebalancingDeltas(
  current: CurrentAllocationEntry[],
  target: RebalancingTarget,
  totalAssetsTWD: number,
): RebalancingDelta[] {
  return current.map(({ bucket, currentTWD, currentPct }) => {
    const targetPct = target[bucket];
    return {
      bucket,
      currentPct,
      targetPct,
      deltaTWD: (targetPct / 100) * totalAssetsTWD - currentTWD,
    };
  });
}
