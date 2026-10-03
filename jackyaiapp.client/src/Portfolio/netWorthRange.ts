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
 * One calendar month earlier, clamped to the end of a shorter month (03-31 → 02-28), which is
 * what the backend's `DateOnly.AddMonths(-1)` does — so the date shown in the input is the date
 * the server actually starts from.
 */
export function minusOneMonth(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  const year = m === 1 ? y - 1 : y;
  const month = m === 1 ? 12 : m - 1;
  const lastDay = new Date(year, month, 0).getDate(); // day 0 of the next month = last of this one
  return `${year}-${pad(month)}-${pad(Math.min(d, lastDay))}`;
}

/**
 * The window the chart opens with: the last month up to today — or, for someone with less than
 * a month of history, everything from their first record. Starting a newer user's chart on an
 * empty stretch before they began tracking would only push their real data to one side.
 */
export function defaultRange(today: string, firstRecordDate?: string): DateRange {
  const monthAgo = minusOneMonth(today);
  return {
    from: firstRecordDate && firstRecordDate > monthAgo ? firstRecordDate : monthAgo,
    to: today,
  };
}

/** Earliest snapshot date, or undefined when there are none yet. */
export function firstRecordDate(effectiveDates: readonly string[]): string | undefined {
  return effectiveDates.reduce<string | undefined>(
    (min, d) => (!min || d < min ? d : min),
    undefined,
  );
}
