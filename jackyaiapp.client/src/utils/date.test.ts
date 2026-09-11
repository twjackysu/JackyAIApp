import { describe, expect, it } from 'vitest';

import { todayISO } from './date';

describe('todayISO', () => {
  it('formats a date as YYYY-MM-DD', () => {
    expect(todayISO(new Date(2026, 8, 11, 13, 30))).toBe('2026-09-11');
  });

  it('zero-pads single-digit months and days', () => {
    expect(todayISO(new Date(2026, 0, 5, 9, 0))).toBe('2026-01-05');
  });

  // The two edges of a local day. East of Greenwich the first instant is still the previous day in
  // UTC (the 00:00-08:00 window that used to mislabel a Taiwan user's snapshot); west of it the
  // second one is already the next day. Both must read as the day the user is actually living in.
  it('reports the local calendar day at both ends of that day', () => {
    expect(todayISO(new Date(2026, 8, 11, 0, 0))).toBe('2026-09-11');
    expect(todayISO(new Date(2026, 8, 11, 23, 59))).toBe('2026-09-11');
  });

  it('disagrees with the UTC rendering whenever the runner is offset from UTC', () => {
    const localMidnight = new Date(2026, 8, 11, 0, 0);
    if (localMidnight.getTimezoneOffset() === 0) return; // nothing to diverge from in UTC
    expect(todayISO(localMidnight)).not.toBe(localMidnight.toISOString().slice(0, 10));
  });
});
