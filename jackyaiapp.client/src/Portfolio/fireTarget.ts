import { readJson, writeJson } from '@/utils/storage';

const STORAGE_KEY = 'portfolio.fireTarget';

/** A per-browser preference, not account data — kept in localStorage rather than the backend,
 * same reasoning as `feeDiscountRate.ts`/`rebalancingTarget.ts`: this is the user's own planning
 * input, not something other users or devices need to see, and not worth a DB table for it. */
export interface FireTarget {
  targetNetWorthTWD: number;
  annualGrowthRatePct: number;
  monthlyContributionTWD: number;
}

export function loadFireTarget(): FireTarget | null {
  return readJson<FireTarget>(STORAGE_KEY);
}

export function saveFireTarget(target: FireTarget): void {
  writeJson(STORAGE_KEY, target);
}

/** Generic placeholder growth-rate assumption, used only while a user has no computable
 * historical annualized return yet (`PortfolioSummaryDto.annualizedReturnPct` is null until 30
 * days of snapshot history exist — see `PortfolioService.ComputeAnnualizedReturn`). Not a
 * measured number; the UI must label it as such rather than let it look like real data. */
export const PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT = 6;

/** `annualizedReturnPct` is nullable by deliberate design (an annualized rate computed from too
 * short a snapshot history is misleading) — never coerce null to 0%, which would show an
 * impossibly pessimistic "will never reach goal" result for a brand-new user. */
export function defaultAnnualGrowthRatePct(annualizedReturnPct: number | null | undefined): number {
  return typeof annualizedReturnPct === 'number' && Number.isFinite(annualizedReturnPct)
    ? annualizedReturnPct
    : PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT;
}

const MAX_YEARS = 100;

/** Year-by-year simulation rather than a closed-form formula — the monthly-contribution term
 * makes a closed form awkward, while a loop handles both a pure lump-sum projection
 * (`monthlyContributionTWD = 0`) and a contribution-aware one with the same code path, and is
 * easy to verify against hand-computed cases. Returns the elapsed whole years once the balance
 * reaches the target, `0` if already there, or `null` if still short after `MAX_YEARS` (never a
 * false precise-looking number past a sane horizon). */
export function computeYearsToGoal(currentNetWorthTWD: number, target: FireTarget): number | null {
  if (currentNetWorthTWD >= target.targetNetWorthTWD) return 0;

  const growthFactor = 1 + target.annualGrowthRatePct / 100;
  const annualContributionTWD = target.monthlyContributionTWD * 12;
  let balance = currentNetWorthTWD;

  for (let year = 1; year <= MAX_YEARS; year++) {
    balance = balance * growthFactor + annualContributionTWD;
    if (balance >= target.targetNetWorthTWD) return year;
  }
  return null;
}
