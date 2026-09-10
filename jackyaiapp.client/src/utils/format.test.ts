import { describe, expect, it } from 'vitest';

import {
  fmtCompact,
  fmtCompactTWD,
  fmtNum,
  fmtQty,
  fmtSigned,
  fmtSignedPct,
  pnlColor,
  signPrefix,
} from './format';

describe('fmtNum', () => {
  it('groups thousands with zero decimals by default', () => {
    expect(fmtNum(1234567)).toBe('1,234,567');
  });

  it('respects an explicit decimals count', () => {
    expect(fmtNum(1234.5, 2)).toBe('1,234.50');
  });
});

describe('fmtQty', () => {
  it('keeps fractional units up to 6 places', () => {
    expect(fmtQty(1000)).toBe('1,000');
    expect(fmtQty(0.123456)).toBe('0.123456');
  });
});

describe('pnlColor', () => {
  it('is green for gains, red for losses, neutral for zero', () => {
    expect(pnlColor(100)).toBe('#22c55e');
    expect(pnlColor(-100)).toBe('#ef4444');
    expect(pnlColor(0)).toBe('text.secondary');
    expect(pnlColor(0, 'inherit')).toBe('inherit');
  });
});

describe('signPrefix', () => {
  it('only prefixes non-negative values — a negative already carries its own minus sign', () => {
    expect(signPrefix(100)).toBe('+');
    expect(signPrefix(0)).toBe('+');
    expect(signPrefix(-100)).toBe('');
  });
});

describe('fmtSigned', () => {
  it('keeps fmtNum grouping and adds an explicit + to gains', () => {
    expect(fmtSigned(1234567)).toBe('+1,234,567');
    expect(fmtSigned(-1234567)).toBe('-1,234,567');
  });

  it('respects an explicit decimals count', () => {
    expect(fmtSigned(3.214, 2)).toBe('+3.21');
  });
});

describe('fmtSignedPct', () => {
  it('adds an explicit + to gains and leaves the minus sign on losses', () => {
    expect(fmtSignedPct(3.214)).toBe('+3.21%');
    expect(fmtSignedPct(-3.214)).toBe('-3.21%');
  });

  it('treats zero as non-negative, matching the green/▲ threshold of the P&L badges', () => {
    expect(fmtSignedPct(0)).toBe('+0.00%');
  });

  it('respects an explicit decimals count', () => {
    expect(fmtSignedPct(3.25, 1)).toBe('+3.3%');
  });
});

describe('fmtCompact', () => {
  it('prints values under 1M in full with grouping', () => {
    expect(fmtCompact(999999)).toBe('999,999');
    expect(fmtCompact(0)).toBe('0');
  });

  it('switches to an M-suffix at 1M with one decimal place', () => {
    expect(fmtCompact(1_000_000)).toBe('1.0M');
    expect(fmtCompact(8_700_000)).toBe('8.7M');
  });

  it('compares by magnitude so negative values (e.g. liabilities) also get the M-suffix', () => {
    expect(fmtCompact(-2_500_000)).toBe('-2.5M');
  });

  it('normalizes negative zero to a plain "0" instead of "-0"', () => {
    expect(fmtCompact(-0)).toBe('0');
  });
});

describe('fmtCompactTWD', () => {
  it('prefixes sub-1M values with NT$', () => {
    expect(fmtCompactTWD(50000)).toBe('NT$50,000');
  });

  it('does not double up the prefix for M-suffixed values', () => {
    expect(fmtCompactTWD(8_700_000)).toBe('8.7M');
  });
});
