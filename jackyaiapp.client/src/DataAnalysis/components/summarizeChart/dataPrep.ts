// Pure data preparation for SummarizeChart — no React, no echarts.

export function inferColumns(
  headers: string[],
  rows: Record<string, unknown>[],
): { groupKeys: string[]; measureKeys: string[] } {
  if (rows.length === 0) return { groupKeys: headers, measureKeys: [] };
  const sample = rows[0];
  const measureKeys: string[] = [];
  const groupKeys: string[] = [];
  headers.forEach((h) => (typeof sample[h] === 'number' ? measureKeys : groupKeys).push(h));
  return { groupKeys, measureKeys };
}

export const trunc = (v: unknown, max = 20): string => {
  const s = String(v ?? '');
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
};

export interface CartesianData {
  /** X-axis (or Y-axis for horizontal) category labels */
  categories: string[];
  /** Each series is one line/bar group */
  series: Array<{ name: string; data: (number | null)[] }>;
  /** True when caller passed 3+ groupKeys; only first two were used */
  dimsTrimmed: boolean;
}

/**
 * Prepare data for all cartesian chart types (column, bar, line, stacked*).
 *
 * 1 groupKey  → categories = dim0 values,  series = one per measure
 * 2 groupKeys → categories = dim0 values,  series = dim1 × measure (pivot)
 * 3+ groupKeys → same as 2-groupKey using only the first two; dimsTrimmed = true
 */
export function prepareCartesian(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKeys: string[],
): CartesianData {
  const dimsTrimmed = groupKeys.length > 2;
  const dims = groupKeys.slice(0, 2); // use at most 2 dimensions

  if (dims.length <= 1) {
    // ── Single dimension ────────────────────────────────────────────────────
    const dim0 = dims[0] ?? '';
    const categories = rows.map((r) => String(r[dim0] ?? ''));
    const series = measureKeys.map((mk) => ({
      name: mk,
      data: rows.map((r) => {
        const v = r[mk];
        return typeof v === 'number' ? v : parseFloat(String(v)) || 0;
      }),
    }));
    return { categories, series, dimsTrimmed };
  }

  // ── Two dimensions: pivot dim1 → series ──────────────────────────────────
  const [dim0, dim1] = dims;

  // Preserve first-seen order for both axes
  const xOrder = new Map<string, number>(); // dim0 value → index
  const sOrder = new Map<string, number>(); // dim1 value → index

  rows.forEach((r) => {
    const x = String(r[dim0] ?? '');
    const s = String(r[dim1] ?? '');
    if (!xOrder.has(x)) xOrder.set(x, xOrder.size);
    if (!sOrder.has(s)) sOrder.set(s, sOrder.size);
  });

  const categories = [...xOrder.keys()];
  const dim1Values = [...sOrder.keys()];

  // Build fast lookup: "xVal|||sVal" → { measureKey: number }
  const lookup = new Map<string, Record<string, number>>();
  rows.forEach((r) => {
    const key = `${String(r[dim0] ?? '')}|||${String(r[dim1] ?? '')}`;
    const nums: Record<string, number> = {};
    measureKeys.forEach((mk) => {
      const v = r[mk];
      nums[mk] = typeof v === 'number' ? v : parseFloat(String(v)) || 0;
    });
    lookup.set(key, nums);
  });

  const series: CartesianData['series'] = [];
  for (const sv of dim1Values) {
    for (const mk of measureKeys) {
      // If there is only one measure, keep series name short (just the dim1 value).
      // With multiple measures, append the measure name so the legend is unambiguous.
      const name = measureKeys.length === 1 ? trunc(sv) : `${trunc(sv, 15)} · ${mk}`;
      const data = categories.map((xc) => {
        const entry = lookup.get(`${xc}|||${sv}`);
        return entry !== undefined ? entry[mk] : null;
      });
      series.push({ name, data });
    }
  }

  return { categories, series, dimsTrimmed };
}

export interface PieData {
  slices: Array<{ name: string; value: number }>;
  /** True when groupKeys.length >= 2; pie only uses dim0 */
  multiDimWarning: boolean;
}

/**
 * Prepare data for pie chart.
 * Only the first groupKey is used as slices; dim1+ is ignored with a warning.
 * If multiple rows share the same dim0 value (due to dim1 being dropped), values are summed.
 */
export function preparePie(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKeys: string[],
): PieData {
  const dim0 = groupKeys[0] ?? '';
  const measure = measureKeys[0];
  const multiDimWarning = groupKeys.length > 1;

  const agg = new Map<string, number>();
  rows.forEach((r) => {
    const name = trunc(String(r[dim0] ?? ''), 18);
    const v = r[measure];
    const val = typeof v === 'number' ? v : parseFloat(String(v)) || 0;
    agg.set(name, (agg.get(name) ?? 0) + val);
  });

  const slices = [...agg.entries()].map(([name, value]) => ({ name, value }));
  return { slices, multiDimWarning };
}
