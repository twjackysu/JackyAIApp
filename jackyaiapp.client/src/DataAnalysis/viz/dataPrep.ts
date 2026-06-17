// Pure data preparation for the Phase 2 visualizations — no React, no echarts.

const num = (v: unknown): number => (typeof v === 'number' ? v : parseFloat(String(v ?? '')) || 0);

// ── Hierarchy (treemap / sunburst / tree) ──────────────────────────────────────

export interface HierNode {
  name: string;
  value?: number;
  children?: HierNode[];
}

/**
 * Nest rows into a tree using the group-by columns in order (each group is one
 * level). Leaf value is the (summed) measure. Internal nodes are left without a
 * value so ECharts sums their descendants.
 */
export function buildHierarchy(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKey: string,
): HierNode[] {
  const root: HierNode = { name: 'root', children: [] };

  for (const r of rows) {
    let cur = root;
    for (const gk of groupKeys) {
      const name = String(r[gk] ?? '');
      cur.children ??= [];
      let child = cur.children.find((c) => c.name === name);
      if (!child) {
        child = { name };
        cur.children.push(child);
      }
      cur = child;
    }
    cur.value = (cur.value ?? 0) + num(r[measureKey]);
  }

  return root.children ?? [];
}

// ── Sankey (flow between consecutive group levels) ──────────────────────────────

export interface SankeyData {
  nodes: { name: string }[];
  links: { source: string; target: string; value: number }[];
  /** Separator used to prefix node ids with their level; strip for labels. */
  sep: string;
}

const SANKEY_SEP = '␟'; // ␟ — unlikely to occur in real values

/**
 * Build sankey nodes/links by treating each adjacent pair of group columns as a
 * flow level. Node ids are prefixed with their level so the same value at
 * different levels stays distinct (and the graph stays acyclic).
 */
export function buildSankey(
  rows: Record<string, unknown>[],
  groupKeys: string[],
  measureKey: string,
): SankeyData {
  const nodes = new Set<string>();
  const links = new Map<string, number>();

  for (const r of rows) {
    const v = num(r[measureKey]);
    if (v <= 0) continue; // sankey needs positive flow
    for (let i = 0; i < groupKeys.length - 1; i++) {
      const src = `${i}${SANKEY_SEP}${String(r[groupKeys[i]] ?? '')}`;
      const tgt = `${i + 1}${SANKEY_SEP}${String(r[groupKeys[i + 1]] ?? '')}`;
      nodes.add(src);
      nodes.add(tgt);
      const key = `${src}→${tgt}`;
      links.set(key, (links.get(key) ?? 0) + v);
    }
  }

  return {
    nodes: [...nodes].map((name) => ({ name })),
    links: [...links].map(([key, value]) => {
      const [source, target] = key.split('→');
      return { source, target, value };
    }),
    sep: SANKEY_SEP,
  };
}

// ── Waterfall (period-over-period changes) ──────────────────────────────────────

export interface WaterfallData {
  categories: string[];
  /** Transparent offset that floats each rise/fall bar to the right level. */
  base: (number | '-')[];
  rise: (number | '-')[];
  fall: (number | '-')[];
  /** The opening bar (first period, drawn from zero). */
  total: (number | '-')[];
}

/**
 * Turn an ordered time series into a bridge waterfall: the first period is a
 * full bar from zero, then each subsequent period is the signed change from the
 * previous period (green up / red down).
 */
export function buildWaterfall(
  rows: Record<string, unknown>[],
  temporalKey: string,
  measureKey: string,
): WaterfallData {
  const sorted = [...rows].sort((a, b) =>
    String(a[temporalKey] ?? '').localeCompare(String(b[temporalKey] ?? '')),
  );
  const categories = sorted.map((r) => String(r[temporalKey] ?? ''));
  const values = sorted.map((r) => num(r[measureKey]));

  const base: (number | '-')[] = [];
  const rise: (number | '-')[] = [];
  const fall: (number | '-')[] = [];
  const total: (number | '-')[] = [];

  let prev = 0;
  values.forEach((v, i) => {
    if (i === 0) {
      base.push(0);
      total.push(v);
      rise.push('-');
      fall.push('-');
      prev = v;
      return;
    }
    const delta = v - prev;
    total.push('-');
    if (delta >= 0) {
      base.push(prev);
      rise.push(delta);
      fall.push('-');
    } else {
      base.push(v);
      rise.push('-');
      fall.push(-delta);
    }
    prev = v;
  });

  return { categories, base, rise, fall, total };
}
