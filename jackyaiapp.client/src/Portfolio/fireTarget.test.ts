import { beforeEach, describe, expect, it } from 'vitest';

import {
  computeYearsToGoal,
  defaultAnnualGrowthRatePct,
  FireTarget,
  loadFireTarget,
  PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT,
  saveFireTarget,
} from './fireTarget';

describe('defaultAnnualGrowthRatePct', () => {
  it('returns the real annualizedReturnPct when it is a finite number', () => {
    expect(defaultAnnualGrowthRatePct(8.5)).toBe(8.5);
    expect(defaultAnnualGrowthRatePct(0)).toBe(0);
    expect(defaultAnnualGrowthRatePct(-3)).toBe(-3);
  });

  it('falls back to the placeholder when the historical rate is null or undefined', () => {
    expect(defaultAnnualGrowthRatePct(null)).toBe(PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT);
    expect(defaultAnnualGrowthRatePct(undefined)).toBe(PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT);
  });
});

describe('computeYearsToGoal', () => {
  it('returns 0 immediately when current net worth already meets or exceeds the target', () => {
    const target: FireTarget = {
      targetNetWorthTWD: 1_000_000,
      annualGrowthRatePct: 5,
      monthlyContributionTWD: 0,
    };
    expect(computeYearsToGoal(1_000_000, target)).toBe(0);
    expect(computeYearsToGoal(2_000_000, target)).toBe(0);
  });

  it('projects a normal multi-year case (growth + contribution) against a hand-computed expectation', () => {
    // current 0, 5%/year growth, NT$10,000/month (120,000/year) contribution, target 500,000:
    // y1: 0*1.05+120000=120000; y2: 120000*1.05+120000=246000;
    // y3: 246000*1.05+120000=378300; y4: 378300*1.05+120000=517215 >= 500000
    const target: FireTarget = {
      targetNetWorthTWD: 500_000,
      annualGrowthRatePct: 5,
      monthlyContributionTWD: 10_000,
    };
    expect(computeYearsToGoal(0, target)).toBe(4);
  });

  it('handles monthlyContributionTWD = 0 as a pure lump-sum projection', () => {
    // 100,000 growing at 10%/year, no contribution, doubling to 200,000:
    // 1.1^7 ≈ 194,871.71 < 200,000 ≤ 1.1^8 ≈ 214,358.88 -> year 8
    const target: FireTarget = {
      targetNetWorthTWD: 200_000,
      annualGrowthRatePct: 10,
      monthlyContributionTWD: 0,
    };
    expect(computeYearsToGoal(100_000, target)).toBe(8);
  });

  it('caps at 100 years and returns null when the target is never reached at this assumption', () => {
    const target: FireTarget = {
      targetNetWorthTWD: 1_000_000_000,
      annualGrowthRatePct: 0,
      monthlyContributionTWD: 0,
    };
    expect(computeYearsToGoal(1, target)).toBeNull();
  });
});

describe('loadFireTarget / saveFireTarget', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('round-trips through localStorage', () => {
    expect(loadFireTarget()).toBeNull();
    const target: FireTarget = {
      targetNetWorthTWD: 10_000_000,
      annualGrowthRatePct: 7,
      monthlyContributionTWD: 20_000,
    };
    saveFireTarget(target);
    expect(loadFireTarget()).toEqual(target);
  });
});
