/**
 * Start/end dates for the 淨值走勢 chart. Dates are ISO `yyyy-MM-dd` strings throughout — the
 * format both `<input type="date">` and the API use — so they compare correctly as strings.
 */

export interface DateRange {
  from: string;
  to: string;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Today in the viewer's local time zone. `toISOString()` would give the UTC date, which in
 * Taiwan is still yesterday until 08:00. */
export function localToday(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

/**
 * The same day `months` calendar months away, clamped to the end of a shorter month
 * (03-31 − 1 → 02-28), which is what the backend's `DateOnly.AddMonths` does — so the date shown
 * in the input is the date the server actually starts from.
 */
export function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const index = y * 12 + (m - 1) + months;
  const year = Math.floor(index / 12);
  const month = (index % 12) + 1;
  const lastDay = new Date(year, month, 0).getDate(); // day 0 of the next month = last of this one
  return `${year}-${pad(month)}-${pad(Math.min(d, lastDay))}`;
}

export function minusOneMonth(iso: string): string {
  return addMonths(iso, -1);
}

/** One-tap windows ending today. Twelve months is the longest the chart allows. */
export const PRESETS = [
  { label: '1M', months: 1 },
  { label: '3M', months: 3 },
  { label: '6M', months: 6 },
  { label: '1Y', months: 12 },
] as const;

/**
 * The window ending today and reaching `months` back — or, when the history is shorter than
 * that, everything from the first record. Starting a chart on an empty stretch before someone
 * began tracking would only push their real data to one side; it also keeps the start input
 * showing the date the data actually starts on.
 */
export function presetRange(today: string, months: number, firstRecordDate?: string): DateRange {
  const start = addMonths(today, -months);
  return {
    from: firstRecordDate && firstRecordDate > start ? firstRecordDate : start,
    to: today,
  };
}

/** The window the chart opens with: the 1M preset. */
export function defaultRange(today: string, firstRecordDate?: string): DateRange {
  return presetRange(today, 1, firstRecordDate);
}

/**
 * The same date `years` calendar years away, clamped for 02-29 (2028-02-29 − 1 → 2027-02-28),
 * which is what the backend's `DateOnly.AddYears` does.
 */
export function addYears(iso: string, years: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const year = y + years;
  const lastDay = new Date(year, m, 0).getDate();
  return `${year}-${pad(m)}-${pad(Math.min(d, lastDay))}`;
}

/**
 * Apply an edit to one end of the window, moving the other end when the edit would break a
 * rule — the edited date always stands as the user typed it:
 * <ul>
 *   <li>start after end (or end before start) → the other end meets it</li>
 *   <li>longer than one year → the other end moves to exactly one year away</li>
 * </ul>
 * Moving the other end, rather than refusing the edit, is what lets someone jump straight to
 * last year's start date without first having to walk the end date back. The server enforces
 * the same one-year limit; doing it here keeps the dates shown equal to the dates charted.
 */
export function applyEdit(range: DateRange, field: 'from' | 'to', value: string): DateRange {
  if (field === 'from') {
    const from = value;
    if (from > range.to) return { from, to: from };
    if (from < addYears(range.to, -1)) return { from, to: addYears(from, 1) };
    return { from, to: range.to };
  }
  const to = value;
  if (to < range.from) return { from: to, to };
  if (range.from < addYears(to, -1)) return { from: addYears(to, -1), to };
  return { from: range.from, to };
}

/** Earliest snapshot date, or undefined when there are none yet. */
export function firstRecordDate(effectiveDates: readonly string[]): string | undefined {
  return effectiveDates.reduce<string | undefined>(
    (min, d) => (!min || d < min ? d : min),
    undefined,
  );
}
