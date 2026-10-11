import { describe, expect, it } from 'vitest';

import {
  computeSeasonal,
  defaultRange,
  formatMonthDay,
  isAxisLabelShown,
  isValidMonthDay,
  lineValuesOnAxis,
  lookupSample,
  parseIsoDate,
  seasonalYears,
  validateRange,
  type DateRange,
  type SeasonalLine,
} from './seasonal';

import type { MarketIndexPoint } from '@/apis/financeApis/types';

const md = (month: number, day: number) => ({ month, day });
const range = (sm: number, sd: number, em: number, ed: number): DateRange => ({
  start: md(sm, sd),
  end: md(em, ed),
});

/** `[year, 'MM-DD', close]` triples → ascending-date points. */
const pts = (...rows: [number, string, number][]): MarketIndexPoint[] =>
  rows.map(([year, key, close]) => ({ date: `${year}-${key}`, close }));

const lineOf = (result: ReturnType<typeof computeSeasonal>, year: number): SeasonalLine => {
  const line = result.lines.find((l) => l.year === year);
  if (!line) throw new Error(`no line for ${year}`);
  return line;
};

describe('validateRange', () => {
  it('accepts an ordinary range, a single day, and 2/29', () => {
    expect(validateRange(range(1, 1, 9, 30))).toBeNull();
    expect(validateRange(range(3, 5, 3, 5))).toBeNull();
    expect(validateRange(range(2, 29, 3, 1))).toBeNull();
  });

  it('rejects days that never exist in any year, before checking order', () => {
    expect(validateRange(range(1, 1, 2, 30))).toBe('invalid-end');
    expect(validateRange(range(4, 31, 9, 30))).toBe('invalid-start');
    // Both impossible and out of order: the impossible date is the thing to fix first.
    expect(validateRange(range(11, 31, 1, 1))).toBe('invalid-start');
  });

  it('rejects a start later than the end', () => {
    expect(validateRange(range(9, 1, 3, 1))).toBe('start-after-end');
    expect(validateRange(range(3, 2, 3, 1))).toBe('start-after-end');
  });
});

describe('isValidMonthDay', () => {
  it('knows the month lengths, with February at 29', () => {
    expect(isValidMonthDay(md(2, 29))).toBe(true);
    expect(isValidMonthDay(md(2, 30))).toBe(false);
    expect(isValidMonthDay(md(6, 31))).toBe(false);
    expect(isValidMonthDay(md(12, 31))).toBe(true);
    expect(isValidMonthDay(md(0, 1))).toBe(false);
    expect(isValidMonthDay(md(13, 1))).toBe(false);
  });
});

describe('parseIsoDate', () => {
  it('parses real dates and rejects malformed or non-existent ones', () => {
    expect(parseIsoDate('2024-02-29')).toEqual({ year: 2024, month: 2, day: 29 });
    expect(parseIsoDate('2025-02-29')).toBeNull();
    expect(parseIsoDate('2025-13-01')).toBeNull();
    expect(parseIsoDate('2025-2-3')).toBeNull();
    expect(parseIsoDate('not a date')).toBeNull();
  });
});

describe('defaultRange / seasonalYears', () => {
  it('defaults to January 1 through the month/day of the latest data', () => {
    expect(defaultRange('2026-10-08')).toEqual(range(1, 1, 10, 8));
  });

  it('covers the data year and the four before it', () => {
    expect(seasonalYears('2026-10-08')).toEqual([2022, 2023, 2024, 2025, 2026]);
  });

  it('rejects an unusable asOf', () => {
    expect(() => defaultRange('2026-02-30')).toThrow(RangeError);
    expect(() => seasonalYears('')).toThrow(RangeError);
  });
});

describe('computeSeasonal', () => {
  const ASOF = '2026-10-08';

  it('rebases each year to 100 at its own first day in the range', () => {
    const result = computeSeasonal(
      pts(
        [2025, '01-02', 200],
        [2025, '01-03', 210],
        [2025, '01-06', 190],
        [2024, '01-02', 1000],
        [2024, '01-03', 1100],
      ),
      ASOF,
      range(1, 1, 1, 31),
    );

    const l2025 = lineOf(result, 2025);
    expect(l2025.samples[0].index).toBe(100);
    expect(l2025.samples[1].index).toBeCloseTo(105, 10);
    expect(l2025.samples[2].index).toBeCloseTo(95, 10);
    expect(l2025.samples[0]).toMatchObject({ key: '01-02', date: '2025-01-02', close: 200 });
    const l2024 = lineOf(result, 2024).samples.map((s) => s.index);
    expect(l2024[0]).toBe(100);
    expect(l2024[1]).toBeCloseTo(110, 10);
  });

  it('moves the baseline to the first trading day after a holiday start', () => {
    // 1/1 is a holiday everywhere; 2024 first trades 1/2, 2023 first trades 1/3.
    const result = computeSeasonal(
      pts([2024, '01-02', 17000], [2024, '01-03', 17100], [2023, '01-03', 14000]),
      ASOF,
      range(1, 1, 1, 31),
    );

    expect(lineOf(result, 2024).samples[0]).toMatchObject({ date: '2024-01-02', index: 100 });
    expect(lineOf(result, 2023).samples[0]).toMatchObject({ date: '2023-01-03', index: 100 });
  });

  it('keeps only days inside a range that spans months', () => {
    const result = computeSeasonal(
      pts(
        [2025, '01-24', 100],
        [2025, '01-27', 101],
        [2025, '02-03', 102],
        [2025, '02-05', 103],
        [2025, '02-06', 104],
      ),
      ASOF,
      range(1, 25, 2, 5),
    );

    expect(lineOf(result, 2025).samples.map((s) => s.key)).toEqual(['01-27', '02-03', '02-05']);
    expect(result.axis[0]).toBe('01-25');
    expect(result.axis[result.axis.length - 1]).toBe('02-05');
    expect(result.axis).toHaveLength(7 + 5); // 1/25–1/31 plus 2/1–2/5
  });

  it('lays out Feb 29 on the axis; non-leap years just have no point there', () => {
    const result = computeSeasonal(
      pts(
        [2024, '02-28', 100],
        [2024, '02-29', 102],
        [2024, '03-01', 101],
        [2025, '02-28', 200],
        [2025, '03-03', 210],
      ),
      ASOF,
      range(2, 28, 3, 1),
    );

    expect(result.axis).toEqual(['02-28', '02-29', '03-01']);
    expect(lineValuesOnAxis(lineOf(result, 2024), result.axis)).toEqual([100, 102, 101]);
    // 2025 has no 2/29 and no 3/1 (a weekend) — only its 2/28 is inside the range.
    expect(lineValuesOnAxis(lineOf(result, 2025), result.axis)).toEqual([100, null, null]);
  });

  it('starting on Feb 29 uses the first day on or after 3/1 in a non-leap year', () => {
    const result = computeSeasonal(
      pts([2024, '02-29', 100], [2025, '02-28', 90], [2025, '03-03', 200], [2025, '03-04', 210]),
      ASOF,
      range(2, 29, 3, 31),
    );

    expect(lineOf(result, 2024).samples[0].date).toBe('2024-02-29');
    expect(lineOf(result, 2025).samples[0]).toMatchObject({ date: '2025-03-03', index: 100 });
    expect(lineOf(result, 2025).samples).toHaveLength(2);
  });

  it('omits a year with no trading day in the range and reports it', () => {
    const result = computeSeasonal(
      pts([2025, '02-10', 100], [2024, '03-01', 100]),
      ASOF,
      range(2, 10, 2, 12),
    );

    expect(result.lines.map((l) => l.year)).toEqual([2025]);
    expect(result.omittedYears).toEqual([2022, 2023, 2024, 2026]);
  });

  it('stops the current year at the latest data and never extends it', () => {
    // Data ends 2026-03-10 (asOf); the window runs to the end of March.
    const asOf = '2026-03-10';
    const result = computeSeasonal(
      pts(
        [2025, '03-03', 100],
        [2025, '03-17', 101],
        [2025, '03-31', 102],
        [2026, '03-02', 500],
        [2026, '03-10', 520],
      ),
      asOf,
      range(3, 1, 3, 31),
    );

    const current = lineOf(result, 2026);
    expect(current.isCurrent).toBe(true);
    expect(current.samples[current.samples.length - 1].date).toBe('2026-03-10');
    const values = lineValuesOnAxis(current, result.axis);
    expect(values[result.axis.indexOf('03-10')]).toBeCloseTo(104, 10);
    expect(values.slice(result.axis.indexOf('03-11')).every((v) => v === null)).toBe(true);
    // Earlier years run the whole window.
    expect(lineOf(result, 2025).isCurrent).toBe(false);
    expect(lineOf(result, 2025).samples[lineOf(result, 2025).samples.length - 1].key).toBe('03-31');
  });

  it('omits the current year when the window starts after the latest data', () => {
    const result = computeSeasonal(
      pts([2026, '03-02', 500], [2025, '11-03', 100]),
      '2026-03-10',
      range(11, 1, 12, 31),
    );

    expect(result.omittedYears).toContain(2026);
    expect(result.lines.map((l) => l.year)).toEqual([2025]);
  });

  it('draws at most five years and ignores older data', () => {
    const result = computeSeasonal(
      pts([2021, '01-04', 100], [2022, '01-03', 100], [2026, '01-05', 100]),
      ASOF,
      range(1, 1, 1, 31),
    );

    expect(result.lines.map((l) => l.year)).toEqual([2022, 2026]);
    expect(result.omittedYears).toEqual([2023, 2024, 2025]);
  });

  it('copes with unsorted input, repeated days and unusable rows', () => {
    const result = computeSeasonal(
      [
        { date: '2025-01-03', close: 110 },
        { date: '2025-01-02', close: 100 },
        { date: '2025-01-03', close: 120 }, // repeated day: the later row wins
        { date: '2025-01-06', close: 0 }, // non-positive
        { date: '2025-01-07', close: Number.NaN },
        { date: '2025-01-08', close: -5 },
        { date: 'garbage', close: 100 },
        { date: '2025-02-30', close: 100 }, // not a real date
      ],
      ASOF,
      range(1, 1, 12, 31),
    );

    expect(lineOf(result, 2025).samples.map((s) => [s.date, s.index])).toEqual([
      ['2025-01-02', 100],
      ['2025-01-03', 120],
    ]);
  });

  it('refuses a range that did not pass validateRange', () => {
    expect(() => computeSeasonal([], ASOF, range(9, 1, 3, 1))).toThrow(RangeError);
    expect(() => computeSeasonal([], ASOF, range(1, 1, 2, 30))).toThrow(RangeError);
  });

  it('reports the latest data day as asOfKey', () => {
    expect(computeSeasonal([], ASOF, range(1, 1, 10, 8)).asOfKey).toBe('10-08');
  });
});

describe('lookupSample', () => {
  const result = computeSeasonal(
    pts([2025, '03-03', 100], [2025, '03-06', 103], [2025, '03-10', 110]),
    '2026-10-08',
    range(3, 1, 3, 31),
  );
  const line = lineOf(result, 2025);

  it('returns the exact day when the market was open', () => {
    const found = lookupSample(line, '03-06', result.asOfKey);
    expect(found).toMatchObject({ kind: 'sample', sample: { date: '2025-03-06', close: 103 } });
  });

  it('carries the previous trading day — with its own date — over a closed day', () => {
    const found = lookupSample(line, '03-08', result.asOfKey);
    expect(found).toMatchObject({ kind: 'sample', sample: { date: '2025-03-06', close: 103 } });
  });

  it('keeps carrying to the end of the window for a past year', () => {
    const found = lookupSample(line, '03-31', result.asOfKey);
    expect(found).toMatchObject({ kind: 'sample', sample: { date: '2025-03-10' } });
  });

  it('has no value before the year’s first trading day in the window', () => {
    expect(lookupSample(line, '03-01', result.asOfKey)).toEqual({ kind: 'not-started' });
    expect(lookupSample(line, '03-02', result.asOfKey)).toEqual({ kind: 'not-started' });
  });

  it('has no data for the current year after the latest data day', () => {
    const asOf = '2026-03-10';
    const current = computeSeasonal(
      pts([2026, '03-02', 500], [2026, '03-10', 520]),
      asOf,
      range(3, 1, 3, 31),
    );
    const l = lineOf(current, 2026);

    expect(lookupSample(l, '03-10', current.asOfKey)).toMatchObject({
      kind: 'sample',
      sample: { date: '2026-03-10' },
    });
    expect(lookupSample(l, '03-11', current.asOfKey)).toEqual({ kind: 'no-data' });
  });

  it('still carries over a holiday in the current year while the data is current', () => {
    const current = computeSeasonal(
      pts([2026, '02-13', 500], [2026, '02-23', 520]),
      '2026-03-10',
      range(2, 1, 3, 10),
    );

    expect(lookupSample(lineOf(current, 2026), '02-18', current.asOfKey)).toMatchObject({
      kind: 'sample',
      sample: { date: '2026-02-13' },
    });
  });
});

describe('lineValuesOnAxis', () => {
  it('puts each rebased close on its day and null on days the market was shut', () => {
    const result = computeSeasonal(
      pts([2025, '05-01', 100], [2025, '05-04', 150]),
      '2026-10-08',
      range(5, 1, 5, 5),
    );

    expect(lineValuesOnAxis(lineOf(result, 2025), result.axis)).toEqual([
      100,
      null,
      null,
      150,
      null,
    ]);
  });
});

describe('axis labels', () => {
  it('labels month starts on a long axis', () => {
    const axis = computeSeasonal([], '2026-10-08', range(1, 1, 9, 30)).axis;
    const shown = axis.filter((_, i) => isAxisLabelShown(axis, i));
    expect(shown).toEqual([
      '01-01',
      '02-01',
      '03-01',
      '04-01',
      '05-01',
      '06-01',
      '07-01',
      '08-01',
      '09-01',
    ]);
  });

  it('labels evenly spaced days on a short axis, starting with the first', () => {
    const axis = computeSeasonal([], '2026-10-08', range(3, 1, 3, 20)).axis; // 20 days
    const shown = axis.filter((_, i) => isAxisLabelShown(axis, i));
    expect(shown[0]).toBe('03-01');
    expect(shown.length).toBeGreaterThanOrEqual(4);
    expect(shown.length).toBeLessThanOrEqual(7);
  });

  it('formats a key as M/D', () => {
    expect(formatMonthDay('02-09')).toBe('2/9');
    expect(formatMonthDay('12-31')).toBe('12/31');
  });
});
