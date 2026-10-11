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
}

export type RebalancingBucket = keyof RebalancingTarget;

export const REBALANCING_BUCKETS: RebalancingBucket[] = ['twStock', 'usStock', 'fund', 'cash'];

/** What was saved before real estate left the allocation: the same buckets plus `realEstate`,
 * all as a % of liquid assets + real estate. */
type LegacyRebalancingTarget = RebalancingTarget & { realEstate?: number };

/** A per-browser preference, not account data — kept in localStorage rather than the backend,
 * same reasoning as `feeDiscountRate.ts`: this is the user's own planning input, not something
 * other users or devices need to see, and not worth a DB table for one small object. */
export function loadRebalancingTarget(): RebalancingTarget | null {
  const saved = readJson<LegacyRebalancingTarget>(STORAGE_KEY);
  if (saved === null || saved.realEstate === undefined) return saved;

  // Saved when real estate was a bucket and every % was of liquid assets + real estate. Rescale
  // the four liquid buckets so they keep their proportions to each other and their old total
  // (usually 100): 台股 30 / 美股 20 / 基金 10 / 現金 10 / 不動產 30 becomes 42.9 / 28.6 / 14.3 /
  // 14.3. With nothing but real estate targeted there is nothing to keep, so start over from
  // the current allocation instead.
  const { realEstate, ...liquid } = saved;
  const total = REBALANCING_BUCKETS.reduce((sum, bucket) => sum + (liquid[bucket] || 0), 0);
  if (total <= 0) return null;
  const scale = (total + realEstate) / total;
  const migrated = {} as RebalancingTarget;
  for (const bucket of REBALANCING_BUCKETS) {
    migrated[bucket] = Math.round((liquid[bucket] || 0) * scale * 10) / 10;
  }
  saveRebalancingTarget(migrated);
  return migrated;
}

export function saveRebalancingTarget(target: RebalancingTarget): void {
  writeJson(STORAGE_KEY, target);
}

/** Bucket → the PortfolioSummaryDto field backing it. Only liquid assets: real estate can't be
 * bought or sold in the amounts a rebalance calls for, and liability isn't something you
 * allocate into — it's a reduction of net worth. */
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
  }
}

/** The base every % in the card is taken of: liquid gross assets (台股 + 美股 + 基金 + 現金),
 * excluding real estate and not net of liability — the same 流動總資產 the headline figures use. */
export function rebalancingBaseTWD(summary: PortfolioSummaryDto): number {
  return summary.liquidGrossAssetsTWD;
}

export interface CurrentAllocationEntry {
  bucket: RebalancingBucket;
  currentTWD: number;
  currentPct: number;
}

/** Current allocation across the 4 liquid buckets, as a % of `rebalancingBaseTWD`. */
export function computeCurrentAllocation(summary: PortfolioSummaryDto): CurrentAllocationEntry[] {
  const totalAssetsTWD = rebalancingBaseTWD(summary);
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
