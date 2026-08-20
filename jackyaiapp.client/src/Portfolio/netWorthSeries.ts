import { NetWorthDataPoint } from '@/apis/portfolioApis/types';

/** Real estate's group key in assetType/category mode (see PortfolioService.GroupKey — it is
 * deliberately "Property", not "RealEstate", to avoid colliding with PortfolioCategory.RealEstate
 * meaning REIT holdings). subCategory/holding modes label it "不動產" instead, which is why every
 * consumer here must fetch with groupBy=assetType for this key to be meaningful. */
export const PROPERTY_KEY = 'Property';

export type Granularity = 'daily' | 'weekly' | 'monthly';

export interface SeriesPoint {
  date: string;
  /** The bucket's closing value (its last day). */
  value: number;
  /** The bucket's opening value (its first day). Only meaningful for the first bucket, where there
   * is no previous close to difference against — see toDeltas. */
  open: number;
}

export interface DeltaPoint {
  date: string;
  delta: number;
}

/** Liquid net worth for one day: everything the point already nets out, minus real estate.
 *
 * `totalTWD` is the sum of every group and liability is ALREADY stored negative (the sign flip
 * happens once, in PortfolioService), so no subtraction of debt is needed here — doing it again
 * would double-count. Only real estate has to come out, because a self-occupied home can't be
 * liquidated, which is the same reasoning that made 流動資產 the headline figure elsewhere. */
export function liquidNetWorth(point: NetWorthDataPoint): number {
  const property = point.groups.find((g) => g.key === PROPERTY_KEY)?.valueTWD ?? 0;
  return point.totalTWD - property;
}

/** Bucket key a date falls into. Weekly buckets are keyed by their Monday so a week is stable
 * regardless of which weekday the range happens to start on. */
function bucketKey(date: string, granularity: Granularity): string {
  if (granularity === 'daily') return date;
  if (granularity === 'monthly') return date.slice(0, 7); // yyyy-MM
  const d = new Date(`${date}T00:00:00Z`);
  // getUTCDay: 0=Sun..6=Sat. Shift back to the Monday that starts this week.
  const daysSinceMonday = (d.getUTCDay() + 6) % 7;
  d.setUTCDate(d.getUTCDate() - daysSinceMonday);
  return d.toISOString().slice(0, 10);
}

/** Collapse a daily series into the requested granularity, taking each bucket's LAST value.
 *
 * Last, not sum or average, because net worth is a stock rather than a flow — "what was it at the
 * end of this week" is the meaningful reading, and it's also what makes the deltas below come out
 * as true period-over-period change. Input is assumed ascending by date (the backend emits it that
 * way); the bucket label is the last real date in the bucket, so a partial trailing period is
 * labelled honestly rather than pretending to be a full one. */
export function bucketSeries(points: SeriesPoint[], granularity: Granularity): SeriesPoint[] {
  if (points.length === 0) return [];

  const out: SeriesPoint[] = [];
  let currentKey = bucketKey(points[0].date, granularity);
  let open = points[0].value;
  let last = points[0];

  for (const p of points) {
    const key = bucketKey(p.date, granularity);
    if (key !== currentKey) {
      out.push({ ...last, open });
      currentKey = key;
      open = p.value; // first value of the new bucket
    }
    last = p;
  }
  out.push({ ...last, open });
  return out;
}

/** Change attributable to each period.
 *
 * Every bucket produces a bar, including the first. Later buckets difference against the previous
 * bucket's close, so the bars reconcile exactly to (last close − first open). The first bucket has
 * no previous close, so it differences against its own open — i.e. "how much did this change since
 * tracking began", which is the natural reading of a bar labelled with that period.
 *
 * The first bucket used to be dropped instead. That silently cost a bar, and for an account with
 * only two months of history it left a single bar where the user reasonably expected two. */
export function toDeltas(series: SeriesPoint[]): DeltaPoint[] {
  return series.map((p, i) => ({
    date: p.date,
    delta: i === 0 ? p.value - p.open : p.value - series[i - 1].value,
  }));
}

/** Full pipeline used by both new charts: daily liquid net worth -> bucketed -> (optionally) diffed. */
export function toLiquidNetWorthSeries(
  points: NetWorthDataPoint[],
  granularity: Granularity,
): SeriesPoint[] {
  return bucketSeries(
    points.map((p) => {
      const v = liquidNetWorth(p);
      return { date: p.date, value: v, open: v };
    }),
    granularity,
  );
}

/** A single bar is not a chart — the same number is already reported as 期間變化 on the trend
 * chart above, so offering a one-bar rendering just invites "is this broken?". Two is the minimum
 * that shows a comparison. */
export const MIN_USEFUL_BARS = 2;

/** How many bars each granularity would actually yield for the data currently loaded.
 *
 * Needed because range and granularity are independent controls over a window the backend caps at
 * 180 days (that cap is not arbitrary — both price sources only fetch 6 months of history, so a
 * longer window would silently understate net worth rather than extend it). That makes some
 * combinations degenerate: a 1-month range bucketed monthly can only ever produce a single bar.
 * The UI uses this to disable those combinations instead of rendering something misleading. */
export function barCountsByGranularity(points: NetWorthDataPoint[]): Record<Granularity, number> {
  // Every bucket yields a bar now that the first one differences against its own open.
  const count = (g: Granularity) => toLiquidNetWorthSeries(points, g).length;
  return { daily: count('daily'), weekly: count('weekly'), monthly: count('monthly') };
}
