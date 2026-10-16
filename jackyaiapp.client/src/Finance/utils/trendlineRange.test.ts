import { afterEach, describe, expect, it } from 'vitest';

import {
  availablePresets,
  latestStart,
  loadRange,
  MIN_RANGE_BARS,
  monthsBefore,
  rangeStartIndex,
  saveRange,
} from './trendlineRange';

/** Weekday dates from `from` up to `to`. */
function weekdays(from: string, to: string): string[] {
  const dates: string[] = [];
  for (let t = Date.parse(from); t <= Date.parse(to); t += 86_400_000) {
    const day = new Date(t).getUTCDay();
    if (day !== 0 && day !== 6) dates.push(new Date(t).toISOString().slice(0, 10));
  }
  return dates;
}

const sixYears = weekdays('2020-10-16', '2026-10-16');
const halfYear = weekdays('2026-04-16', '2026-10-16');

describe('monthsBefore', () => {
  it('goes back whole months', () => {
    expect(monthsBefore('2026-10-16', 12)).toBe('2025-10-16');
    expect(monthsBefore('2026-10-16', 3)).toBe('2026-07-16');
  });
});

describe('availablePresets', () => {
  it('offers every range six years of history can show', () => {
    expect(availablePresets(sixYears).map((p) => p.id)).toEqual([
      '3m',
      '6m',
      '1y',
      '2y',
      '5y',
      'all',
    ]);
  });

  it('drops the ranges a stock’s half year cannot tell apart from 全部', () => {
    expect(availablePresets(halfYear).map((p) => p.id)).toEqual(['3m', 'all']);
  });
});

describe('rangeStartIndex', () => {
  it('starts a preset that many months before the last bar', () => {
    expect(sixYears[rangeStartIndex(sixYears, { preset: '1y' })]).toBe('2025-10-16');
    expect(rangeStartIndex(sixYears, { preset: 'all' })).toBe(0);
  });

  it('starts at a picked date, or the next bar after it', () => {
    expect(sixYears[rangeStartIndex(sixYears, { from: '2023-03-04' })]).toBe('2023-03-06');
    expect(rangeStartIndex(sixYears, { from: '2010-01-01' })).toBe(0);
  });

  it('always leaves enough bars to find lines on', () => {
    const latest = sixYears.length - MIN_RANGE_BARS;
    expect(rangeStartIndex(sixYears, { from: '2026-10-01' })).toBe(latest);
    expect(rangeStartIndex(sixYears, { from: '2030-01-01' })).toBe(latest);
    expect(latestStart(sixYears)).toBe(sixYears[latest]);
    // A range longer than the history is all of it.
    expect(rangeStartIndex(halfYear, { preset: '5y' })).toBe(0);
  });
});

describe('storage', () => {
  afterEach(() => localStorage.clear());

  it('remembers the choice per code and ignores one it cannot use', () => {
    saveRange('TAIEX', { preset: '5y' });
    saveRange('2330', { from: '2026-06-01' });
    expect(loadRange('TAIEX')).toEqual({ preset: '5y' });
    expect(loadRange('2330')).toEqual({ from: '2026-06-01' });
    localStorage.setItem('finance.trendlineRange.X', JSON.stringify({ preset: '10y' }));
    localStorage.setItem('finance.trendlineRange.Y', JSON.stringify({ from: 'yesterday' }));
    expect(loadRange('X')).toBeNull();
    expect(loadRange('Y')).toBeNull();
    expect(loadRange('none')).toBeNull();
  });
});
