const DAY_MS = 24 * 60 * 60 * 1000;
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();

/** Which heading of the history a conversation last touched at `iso` goes under, seen on `now`. */
export const periodOf = (iso: string, now: Date = new Date()): string => {
  const days = Math.round((startOfDay(now) - startOfDay(new Date(iso))) / DAY_MS);
  if (days <= 0) return '今天';
  if (days === 1) return '昨天';
  if (days < 7) return '過去 7 天';
  if (days < 30) return '過去 30 天';
  return '更早';
};
