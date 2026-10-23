import type { SummarizeRequest } from '../../../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from '../../types';

export type SummarizeMode = 'manual' | 'ai';

export interface ColumnHints {
  /** Output column names of every group-by. */
  groupHints: string[];
  /** Output column names of the DateTrunc group-bys. */
  temporalHints: string[];
}

/**
 * Which result columns are dimensions (group-bys) and which are temporal, straight from the query —
 * so a numeric-valued dimension (e.g. DateTrunc/Year → 2024) isn't mistaken for a measure. Output
 * column name is alias ?? fieldName for both modes; a manual group-by is temporal only when it
 * applies a DateTrunc transform.
 */
export function resultColumnHints(
  mode: SummarizeMode,
  nlPlan: SummarizeRequest | undefined,
  viewByEntries: ViewByEntry[],
  fieldTransforms: FieldTransformDef[],
): ColumnHints {
  if (mode === 'ai') {
    const vb = nlPlan?.viewBy ?? [];
    return {
      groupHints: vb.map((g) => g.alias || g.fieldName),
      temporalHints: vb.filter((g) => g.type === 'DateTrunc').map((g) => g.alias || g.fieldName),
    };
  }
  const isDateTrunc = (e: ViewByEntry) =>
    e.type === 'Transform' &&
    e.transformId != null &&
    fieldTransforms.find((t) => t.id === e.transformId)?.type === 'DateTrunc';
  return {
    groupHints: viewByEntries.map((e) => e.alias || e.fieldName),
    temporalHints: viewByEntries.filter(isDateTrunc).map((e) => e.alias || e.fieldName),
  };
}

/**
 * The re-runnable plan behind the current result, for "Add to dashboard". AI: the returned plan +
 * the page's source context. Manual: the exact last run.
 */
export function dashboardPlan(
  mode: SummarizeMode,
  nlPlan: SummarizeRequest | undefined,
  summarizeLastRequest: SummarizeRequest | null,
  sourceId: string | undefined,
  tableName: string | undefined,
): SummarizeRequest | null {
  if (mode === 'ai') {
    if (!nlPlan) return null;
    return {
      context: sourceId ? { sourceId, tableName } : undefined,
      viewBy: nlPlan.viewBy,
      measures: nlPlan.measures,
      filters: nlPlan.filters,
    };
  }
  return summarizeLastRequest;
}

export const MAX_RECENT_QUERIES = 5;

/** The recent questions after asking `query`: it first, once, and at most five in all. */
export function rememberQuery(recent: string[], query: string): string[] {
  return [query, ...recent.filter((p) => p !== query)].slice(0, MAX_RECENT_QUERIES);
}
