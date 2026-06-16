import type { ReactNode } from 'react';

// ── Visualization identifiers ──────────────────────────────────────────────────
// Phase 1 set. Phase 2 will extend this union (kpi, treemap, sunburst, tree,
// waterfall, sankey, donut).
export type VizId = 'table' | 'column' | 'bar' | 'line' | 'pie' | 'stackedColumn' | 'stackedBar';

/** Pixel size of a widget. */
export interface VizSize {
  w: number;
  h: number;
}

/**
 * Declarative capability/constraints for one visualization. The matcher uses
 * these to decide whether a viz can render a given result shape, so each
 * component states its own limits in one place instead of scattering ad-hoc
 * checks across the rendering code.
 */
export interface VizCapability {
  id: VizId;
  label: string;
  icon: ReactNode;
  /** Inclusive bounds on the number of group-by (dimension) columns. */
  minGroups: number;
  maxGroups: number; // use Infinity for unbounded (table, hierarchical)
  /** Inclusive bounds on the number of numeric measure columns. */
  minMeasures: number;
  maxMeasures: number; // use Infinity for unbounded
  /** Requires a temporal (date/time) dimension to be meaningful. */
  requiresTemporal?: boolean;
  /** Minimum distinct time points (e.g. waterfall needs ≥2 to show change). */
  minTemporalPoints?: number;
  /** Hierarchical viz — consumes each group-by as one nesting level. */
  hierarchical?: boolean;
  /** Initial widget size when this viz is first shown. */
  defaultSize: VizSize;
}

/**
 * Shape of a summarize result, derived once and shared by the matcher and the
 * renderers.
 */
export interface DataShape {
  groupKeys: string[];
  measureKeys: string[];
  groupCount: number;
  measureCount: number;
  rowCount: number;
  /** Output column that is a date/time dimension, if any. */
  temporalKey?: string;
  /** Distinct values in the temporal column. */
  temporalPoints?: number;
}

export interface EligibilityResult {
  ok: boolean;
  /** Human-readable reason (zh-TW) shown in the disabled toggle tooltip. */
  reason?: string;
}
