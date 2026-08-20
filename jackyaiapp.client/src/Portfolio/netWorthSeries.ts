import { NetWorthDataPoint } from '@/apis/portfolioApis/types';

/** Real estate's group key in assetType/category mode (see PortfolioService.GroupKey — it is
 * deliberately "Property", not "RealEstate", to avoid colliding with PortfolioCategory.RealEstate
 * meaning REIT holdings). subCategory/holding modes label it "不動產" instead, which is why every
 * consumer here must fetch with groupBy=assetType for this key to be meaningful. */
export const PROPERTY_KEY = 'Property';

export type Granularity = 'daily' | 'weekly' | 'monthly';

export interface SeriesPoint {
  date: string;
  value: number;
}

export interface DeltaPoint {
  date: string;
  /** Null for the first bucket — there is no previous period to difference against. Deliberately
   * not 0: a 0 bar would read as "nothing changed this period", which is a different and false
   * claim than "unknown". */
  delta: number | null;
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
  let last = points[0];

  for (const p of points) {
    const key = bucketKey(p.date, granularity);
    if (key !== currentKey) {
      out.push(last);
      currentKey = key;
    }
    last = p;
  }
  out.push(last);
  return out;
}

/** Period-over-period change. The first entry's delta is null — see DeltaPoint.delta. */
export function toDeltas(series: SeriesPoint[]): DeltaPoint[] {
  return series.map((p, i) => ({
    date: p.date,
    delta: i === 0 ? null : p.value - series[i - 1].value,
  }));
}

/** Full pipeline used by both new charts: daily liquid net worth -> bucketed -> (optionally) diffed. */
export function toLiquidNetWorthSeries(
  points: NetWorthDataPoint[],
  granularity: Granularity,
): SeriesPoint[] {
  return bucketSeries(
    points.map((p) => ({ date: p.date, value: liquidNetWorth(p) })),
    granularity,
  );
}

/** Fewer bars than this and a delta chart says nothing — one bar in particular is indistinguishable
 * from "the feature is broken", which is exactly how it was first reported. */
export const MIN_USEFUL_BARS = 3;

/** How many bars each granularity would actually yield for the data currently loaded.
 *
 * Needed because range and granularity are independent controls over a window the backend caps at
 * 180 days (that cap is not arbitrary — both price sources only fetch 6 months of history, so a
 * longer window would silently understate net worth rather than extend it). That makes some
 * combinations degenerate: a 1-month range bucketed monthly can only ever produce a single bar.
 * The UI uses this to disable those combinations instead of rendering something misleading. */
export function barCountsByGranularity(points: NetWorthDataPoint[]): Record<Granularity, number> {
  const count = (g: Granularity) => Math.max(0, toLiquidNetWorthSeries(points, g).length - 1); // -1: first bucket has no predecessor
  return { daily: count('daily'), weekly: count('weekly'), monthly: count('monthly') };
}
