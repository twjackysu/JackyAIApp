/**
 * Today's date on the *user's own calendar*, as `YYYY-MM-DD`.
 *
 * Deliberately built from the local getters instead of the shorter
 * `new Date().toISOString().slice(0, 10)`, because `toISOString()` renders the instant in UTC:
 * east of Greenwich it hands back *yesterday* for the whole early-morning window (00:00-08:00 in
 * Taiwan, UTC+8). Every caller here means "the day the user is currently living in", so that
 * window silently produced the wrong date — a Portfolio snapshot saved at 01:00 landed on
 * yesterday's date and replaced yesterday's snapshot, and the Finance page captioned itself with
 * yesterday.
 *
 * Only for "what day is it for this user" values. A date that is *already* a `YYYY-MM-DD` string
 * and only needs bucketing/arithmetic should keep parsing it as UTC (see `netWorthSeries`), where
 * UTC is the right choice precisely because it has no offset to drift across.
 *
 * @param now injectable clock, so tests can pin a date instead of chasing the real calendar.
 */
export function todayISO(now: Date = new Date()): string {
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}
