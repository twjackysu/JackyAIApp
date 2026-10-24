import { VIZ_CAPABILITIES } from '../viz/capabilities';

import type { VizId } from '../viz/types';
import type { SummarizeRequest } from '@/apis/dataAnalysisApis/types';

/**
 * A `chart` card's data (docs/ai-native/design/04 §2.1), as the Data Analysis assistant's tools
 * send it: a summarize result, the chart to draw it as, and the plan that re-runs it, which is
 * what a dashboard widget stores. It comes from a tool, so every field is checked here and the
 * card is dropped when one is not what it should be.
 */
export interface ChartCardData {
  viz: VizId;
  headers: string[];
  rows: Record<string, string | number | null>[];
  /** Columns the plan grouped by, and which of them are dates (the shape's hints). */
  groupHints: string[];
  temporalHints: string[];
  /** Re-runs the result; with it the card can be added to a dashboard. */
  plan: SummarizeRequest | null;
}

export const CHART_LIMITS = { columns: 20, rows: 500 } as const;

const VIZ_IDS = new Set<string>(VIZ_CAPABILITIES.map((c) => c.id));

const strings = (v: unknown, max: number): string[] | null =>
  Array.isArray(v) && v.every((s) => typeof s === 'string') ? (v as string[]).slice(0, max) : null;

const value = (v: unknown): string | number | null =>
  typeof v === 'number' && Number.isFinite(v) ? v : typeof v === 'string' ? v.slice(0, 500) : null;

const isPlan = (v: unknown): v is SummarizeRequest =>
  typeof v === 'object' &&
  v !== null &&
  Array.isArray((v as SummarizeRequest).viewBy) &&
  Array.isArray((v as SummarizeRequest).measures);

export function parseChartCardData(data: Record<string, unknown>): ChartCardData | null {
  if (typeof data.viz !== 'string' || !VIZ_IDS.has(data.viz)) return null;
  const headers = strings(data.headers, CHART_LIMITS.columns);
  if (!headers?.length || !Array.isArray(data.rows)) return null;
  const rows = data.rows
    .filter((r): r is Record<string, unknown> => typeof r === 'object' && r !== null)
    .slice(0, CHART_LIMITS.rows)
    .map((r) => Object.fromEntries(headers.map((h) => [h, value(r[h])])));
  return {
    viz: data.viz as VizId,
    headers,
    rows,
    groupHints: strings(data.groupHints, CHART_LIMITS.columns) ?? [],
    temporalHints: strings(data.temporalHints, CHART_LIMITS.columns) ?? [],
    plan: isPlan(data.plan) ? data.plan : null,
  };
}
