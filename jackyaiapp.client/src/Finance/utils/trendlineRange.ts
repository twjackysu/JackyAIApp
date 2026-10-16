/**
 * Which part of the price history the trendline chart shows. The automatic lines are found on the
 * shown part only, so a later start date gives the lines of a shorter trend.
 */

import { readJson, writeJson } from '@/utils/storage';

export type RangePresetId = '3m' | '6m' | '1y' | '2y' | '5y' | 'all';

export interface RangePreset {
  id: RangePresetId;
  label: string;
  /** Months back from the last bar; null for all of it. */
  months: number | null;
}

/** Shortcuts only — any start date can be picked — and few enough to sit on one line on a phone. */
export const RANGE_PRESETS: RangePreset[] = [
  { id: '3m', label: '3個月', months: 3 },
  { id: '6m', label: '半年', months: 6 },
  { id: '1y', label: '1年', months: 12 },
  { id: '2y', label: '2年', months: 24 },
  { id: '5y', label: '5年', months: 60 },
  { id: 'all', label: '全部', months: null },
];

/** A preset, or a start date the user picked. */
export type RangeChoice = { preset: RangePresetId } | { from: string };

/** Fewest bars a start date may leave: the lines need a few swings to go on. */
export const MIN_RANGE_BARS = 40;
/** A preset that starts within this many days of the first bar is the same as 全部. */
const SAME_AS_ALL_DAYS = 7;

const isoDate = /^\d{4}-\d{2}-\d{2}$/;

/** `date` (YYYY-MM-DD) moved back `months` months. */
export function monthsBefore(date: string, months: number): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1 - months, d)).toISOString().slice(0, 10);
}

/** The latest start date that still leaves {@link MIN_RANGE_BARS} bars. */
export const latestStart = (dates: string[]) => dates[Math.max(0, dates.length - MIN_RANGE_BARS)];

/** The presets that show something different on these bars, always ending with 全部. */
export function availablePresets(dates: string[]): RangePreset[] {
  const first = Date.parse(dates[0]);
  const latest = latestStart(dates);
  return RANGE_PRESETS.filter((p) => {
    if (p.months === null) return true;
    const from = monthsBefore(dates[dates.length - 1], p.months);
    return (Date.parse(from) - first) / 86_400_000 > SAME_AS_ALL_DAYS && from <= latest;
  });
}

/** Index of the first bar shown for `choice`, never leaving fewer than {@link MIN_RANGE_BARS}. */
export function rangeStartIndex(dates: string[], choice: RangeChoice): number {
  const preset = 'preset' in choice ? RANGE_PRESETS.find((p) => p.id === choice.preset) : null;
  const from =
    'from' in choice
      ? choice.from
      : preset?.months != null
        ? monthsBefore(dates[dates.length - 1], preset.months)
        : null;
  if (from === null) return 0;
  const index = dates.findIndex((d) => d >= from);
  const latest = Math.max(0, dates.length - MIN_RANGE_BARS);
  return index < 0 ? latest : Math.min(index, latest);
}

// ── Storage ───────────────────────────────────────────────────────────────────

const storageKey = (code: string) => `finance.trendlineRange.${code}`;

export function loadRange(code: string): RangeChoice | null {
  const stored = readJson<RangeChoice>(storageKey(code));
  if (stored === null || typeof stored !== 'object') return null;
  if ('preset' in stored && RANGE_PRESETS.some((p) => p.id === stored.preset)) return stored;
  if ('from' in stored && typeof stored.from === 'string' && isoDate.test(stored.from))
    return stored;
  return null;
}

export function saveRange(code: string, choice: RangeChoice): void {
  writeJson(storageKey(code), choice);
}
