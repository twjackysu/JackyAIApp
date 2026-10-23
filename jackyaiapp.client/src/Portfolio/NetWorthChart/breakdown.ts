import { seriesColor, seriesLabel } from './palette';

import { NetWorthDataPoint, NetWorthGroupBy, NetWorthGroupValue } from '@/apis/portfolioApis/types';

// "liquidity" isn't a real backend groupBy mode — it's a client-side re-bucketing of the same
// assetType-mode response (TwStock/UsStock/Cash/Fund collapsed into one "動產" series, Property
// kept as "不動產", Liability kept as-is), so selecting it fetches with groupBy=assetType under
// the hood rather than adding a new backend enum member/endpoint param for what's really just a
// different way to look at data we already have.
export type ViewBy = NetWorthGroupBy | 'liquidity';

export const VIEW_BY_OPTIONS: { label: string; value: ViewBy }[] = [
  { label: '資產類型', value: 'assetType' },
  { label: '動產/不動產', value: 'liquidity' },
  { label: '產業', value: 'category' },
  { label: '子類別', value: 'subCategory' },
  { label: '個股明細', value: 'holding' },
];

const LIQUID_KEY = 'Liquid';

/** What a view asks the server for: 動產/不動產 rides on the asset-type response. */
export const queryGroupByOf = (view: ViewBy): NetWorthGroupBy =>
  view === 'liquidity' ? 'assetType' : view;

/** Collapse an assetType-mode day's groups into 動產(Liquid)/不動產(Property)/負債(Liability). */
function toLiquidityGroups(groups: NetWorthGroupValue[]): NetWorthGroupValue[] {
  let liquid = 0,
    property = 0,
    liability = 0;
  for (const g of groups) {
    if (g.key === 'Property') property += g.valueTWD;
    else if (g.key === 'Liability') liability += g.valueTWD;
    else liquid += g.valueTWD;
  }
  return [
    { key: LIQUID_KEY, valueTWD: liquid },
    { key: 'Property', valueTWD: property },
    { key: 'Liability', valueTWD: liability },
  ].filter((g) => g.valueTWD !== 0);
}

/** The days as the view shows them. */
export const pointsForView = (points: NetWorthDataPoint[], view: ViewBy): NetWorthDataPoint[] =>
  view === 'liquidity'
    ? points.map((p) => ({ ...p, groups: toLiquidityGroups(p.groups) }))
    : points;

export interface BreakdownSeries {
  key: string;
  name: string;
  color: string;
  /** One value per day; 0 on a day the group is not there. */
  values: number[];
}

/**
 * One series per group seen anywhere in the range — different days can have different active
 * groups (e.g. a category only appears once its symbol gets held/classified) — leaving out any
 * that is zero throughout.
 */
export function breakdownOf(points: NetWorthDataPoint[]): BreakdownSeries[] {
  const allKeys = Array.from(new Set(points.flatMap((p) => p.groups.map((g) => g.key))));
  return allKeys
    .map((key) => ({
      key,
      name: seriesLabel(key),
      color: seriesColor(key),
      values: points.map((p) => p.groups.find((g) => g.key === key)?.valueTWD ?? 0),
    }))
    .filter((s) => s.values.some((v) => v !== 0));
}
