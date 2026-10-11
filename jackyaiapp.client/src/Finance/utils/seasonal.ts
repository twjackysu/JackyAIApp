import type { MarketIndexPoint } from '@/apis/financeApis/types';

// Pure math behind the "過去 5 年同期間走勢" chart (MarketSeasonalChart): which trading days of
// each year fall inside a month/day window, rebased so each year's first day in the window is 100.
// No React, no ECharts — everything here is unit-tested directly.

/** Number of lines on the chart: the data year plus the four before it (REQUIREMENTS A16). */
export const SEASONAL_YEAR_COUNT = 5;

/** Days per month of a leap year: the chart's calendar must have a slot for 2/29, and the date
 * pickers must accept it (non-leap years simply have no point there). */
const DAYS_IN_MONTH = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

/** A calendar month/day with no year. */
export interface MonthDay {
  month: number;
  day: number;
}

export interface DateRange {
  start: MonthDay;
  end: MonthDay;
}

export type RangeProblem = 'invalid-start' | 'invalid-end' | 'start-after-end';

/** One trading day on a line. `key` is the zero-padded `MM-DD` the x-axis is laid out on. */
export interface SeasonalSample {
  key: string;
  /** The real trading date (`YYYY-MM-DD`) — shown in the tooltip, never rounded to another day. */
  date: string;
  close: number;
  /** `close` rebased so the line's first sample is 100. */
  index: number;
}

export interface SeasonalLine {
  year: number;
  /** True for the data year — its line stops at `asOf` and is never extended. */
  isCurrent: boolean;
  /** Ascending by date; `samples[0].index` is exactly 100. Never empty. */
  samples: SeasonalSample[];
}

export interface SeasonalResult {
  /** Every `MM-DD` of the (leap) reference year from the range start to its end, inclusive. */
  axis: string[];
  /** Years that have at least one trading day in the range, ascending. */
  lines: SeasonalLine[];
  /** Years in the window that have no trading day in the range, ascending (no line is drawn). */
  omittedYears: number[];
  /** `MM-DD` of `asOf`; hovering later than this on the current year's line has no data. */
  asOfKey: string;
}

export type SeasonalLookup =
  /** `sample.key` is earlier than the hovered key when that year was closed on the hovered day. */
  | { kind: 'sample'; sample: SeasonalSample }
  /** The hovered day is before the year's first trading day in the range. */
  | { kind: 'not-started' }
  /** The current year has not reached the hovered day yet. */
  | { kind: 'no-data' };

const pad2 = (n: number) => String(n).padStart(2, '0');

const monthDayOrder = ({ month, day }: MonthDay) => month * 100 + day;

export const monthDayKey = ({ month, day }: MonthDay) => `${pad2(month)}-${pad2(day)}`;

export const isValidMonthDay = ({ month, day }: MonthDay) =>
  Number.isInteger(month) &&
  Number.isInteger(day) &&
  month >= 1 &&
  month <= 12 &&
  day >= 1 &&
  day <= DAYS_IN_MONTH[month - 1];

interface ParsedDate extends MonthDay {
  year: number;
}

/** Parses `YYYY-MM-DD`; null for anything else (including dates that don't exist). */
export function parseIsoDate(iso: string): ParsedDate | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  // Date.UTC rolls 2/30 over to 3/2, so a round trip is the cheapest "does this day exist" check
  // that also knows which years are leap years.
  const probe = new Date(Date.UTC(year, month - 1, day));
  if (
    probe.getUTCFullYear() !== year ||
    probe.getUTCMonth() !== month - 1 ||
    probe.getUTCDate() !== day
  ) {
    return null;
  }
  return { year, month, day };
}

/** The window's default: 1/1 through the month/day of the latest data (年初至今). */
export function defaultRange(asOf: string): DateRange {
  const parsed = parseIsoDate(asOf);
  if (!parsed) throw new RangeError(`Invalid asOf date: ${asOf}`);
  return { start: { month: 1, day: 1 }, end: { month: parsed.month, day: parsed.day } };
}

/** Why the range can't be drawn, or null when it can. Non-existent days (2/30) are reported
 * before ordering, since "start after end" is meaningless with an impossible date. */
export function validateRange({ start, end }: DateRange): RangeProblem | null {
  if (!isValidMonthDay(start)) return 'invalid-start';
  if (!isValidMonthDay(end)) return 'invalid-end';
  if (monthDayOrder(start) > monthDayOrder(end)) return 'start-after-end';
  return null;
}

/** The years drawn, ascending: `asOf`'s year and the four before it. */
export function seasonalYears(asOf: string): number[] {
  const parsed = parseIsoDate(asOf);
  if (!parsed) throw new RangeError(`Invalid asOf date: ${asOf}`);
  return Array.from(
    { length: SEASONAL_YEAR_COUNT },
    (_, i) => parsed.year - (SEASONAL_YEAR_COUNT - 1) + i,
  );
}

const REFERENCE_YEAR = 2000; // a leap year
const DAY_MS = 24 * 60 * 60 * 1000;

function buildAxis({ start, end }: DateRange): string[] {
  const first = Date.UTC(REFERENCE_YEAR, start.month - 1, start.day);
  const last = Date.UTC(REFERENCE_YEAR, end.month - 1, end.day);
  const axis: string[] = [];
  for (let t = first; t <= last; t += DAY_MS) {
    axis.push(new Date(t).toISOString().slice(5, 10));
  }
  return axis;
}

/**
 * Lays out the trading days of the last {@link SEASONAL_YEAR_COUNT} years that fall inside `range`.
 *
 * Each year's baseline is its first trading day on or after the range start (a holiday start just
 * moves the baseline later); that day's close is 100. A year with no trading day in the range
 * gets no line and is reported in `omittedYears`. The current year contains only what has
 * happened — nothing is extrapolated. `range` must already pass {@link validateRange}.
 */
export function computeSeasonal(
  points: MarketIndexPoint[],
  asOf: string,
  range: DateRange,
): SeasonalResult {
  const problem = validateRange(range);
  if (problem) throw new RangeError(`Invalid seasonal range: ${problem}`);
  const asOfDate = parseIsoDate(asOf);
  if (!asOfDate) throw new RangeError(`Invalid asOf date: ${asOf}`);

  const years = seasonalYears(asOf);
  const startOrder = monthDayOrder(range.start);
  const endOrder = monthDayOrder(range.end);

  // One entry per date even if the feed repeats a day (the later one wins); order never assumed.
  const byYear = new Map<number, Map<string, { parsed: ParsedDate; close: number }>>(
    years.map((y) => [y, new Map()]),
  );
  for (const p of points) {
    const parsed = parseIsoDate(p.date);
    if (!parsed || !Number.isFinite(p.close) || p.close <= 0) continue;
    const order = monthDayOrder(parsed);
    if (order < startOrder || order > endOrder) continue;
    byYear.get(parsed.year)?.set(p.date, { parsed, close: p.close });
  }

  const lines: SeasonalLine[] = [];
  const omittedYears: number[] = [];
  for (const year of years) {
    const days = [...(byYear.get(year) ?? [])].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    if (days.length === 0) {
      omittedYears.push(year);
      continue;
    }
    const baseClose = days[0][1].close;
    lines.push({
      year,
      isCurrent: year === asOfDate.year,
      samples: days.map(([date, { parsed, close }]) => ({
        key: monthDayKey(parsed),
        date,
        close,
        index: (close / baseClose) * 100,
      })),
    });
  }

  return {
    axis: buildAxis(range),
    lines,
    omittedYears,
    asOfKey: monthDayKey(asOfDate),
  };
}

/** The line's values laid out on `axis`: the rebased close on days the market was open, `null` on
 * the rest (the chart bridges those, so a closed day doesn't break the line). */
export function lineValuesOnAxis(line: SeasonalLine, axis: string[]): (number | null)[] {
  const byKey = new Map(line.samples.map((s) => [s.key, s.index]));
  return axis.map((key) => byKey.get(key) ?? null);
}

/**
 * What the tooltip shows for `line` at `key` (`MM-DD`): the most recent trading day not after it,
 * so a day the market was closed repeats the previous close under that earlier day's own date
 * instead of erroring or leaving a blank.
 */
export function lookupSample(line: SeasonalLine, key: string, asOfKey: string): SeasonalLookup {
  if (line.isCurrent && key > asOfKey) return { kind: 'no-data' };
  // Binary search for the last sample whose key <= hovered key (`MM-DD` strings sort correctly).
  let lo = 0;
  let hi = line.samples.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (line.samples[mid].key <= key) {
      found = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return found < 0 ? { kind: 'not-started' } : { kind: 'sample', sample: line.samples[found] };
}

/** `MM-DD` → `M/D` for display. */
export function formatMonthDay(key: string): string {
  const [month, day] = key.split('-');
  return `${Number(month)}/${Number(day)}`;
}

/**
 * Whether the x-axis prints a label at `index`. Long windows label the first of each month; a
 * window of 45 days or fewer has too few month starts to be useful, so it labels evenly spaced days.
 */
export function isAxisLabelShown(axis: string[], index: number): boolean {
  if (axis.length <= 45) {
    const step = Math.max(1, Math.ceil(axis.length / 6));
    return index % step === 0;
  }
  return axis[index]?.endsWith('-01') ?? false;
}
