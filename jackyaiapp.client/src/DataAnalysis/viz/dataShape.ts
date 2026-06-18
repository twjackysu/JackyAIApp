import { inferColumns } from '../components/summarizeChart/dataPrep';

import type { DataShape } from './types';
import type { GroupFieldDto } from '../../apis/dataAnalysisApis/types';

/**
 * Derive viz hints from a query plan's viewBy list: every group-by output column
 * (alias ?? fieldName) is a dimension, and DateTrunc ones are temporal.
 */
export function deriveViewByHints(viewBy: GroupFieldDto[]): {
  groupHints: string[];
  temporalHints: string[];
} {
  return {
    groupHints: viewBy.map((g) => g.alias || g.fieldName),
    temporalHints: viewBy.filter((g) => g.type === 'DateTrunc').map((g) => g.alias || g.fieldName),
  };
}

const TEMPORAL_NAME_RE = /date|month|year|week|quarter|day|time|時間|日期|月|季|年|週/i;
const TEMPORAL_VALUE_RE = /^\d{4}([-/]\d{1,2})?/; // 2024, 2024-03, 2024/03, 2024Q1…

/**
 * Decide whether a group column is temporal. Prefers explicit hints (the output
 * column names of DateTrunc group-bys from the query plan) and falls back to a
 * name/value heuristic.
 */
function detectTemporalKey(
  groupKeys: string[],
  rows: Record<string, unknown>[],
  temporalHints: string[],
): string | undefined {
  const hinted = groupKeys.find((k) => temporalHints.includes(k));
  if (hinted) return hinted;

  return groupKeys.find((k) => {
    if (TEMPORAL_NAME_RE.test(k)) return true;
    const firstVal = String(rows[0]?.[k] ?? '');
    return TEMPORAL_VALUE_RE.test(firstVal);
  });
}

interface ShapeHints {
  /**
   * Output column names (alias ?? fieldName) of ALL group-bys in the query.
   * These are forced to be dimensions regardless of value type — without this a
   * numeric-valued dimension (e.g. DateTrunc/Year → 2024) is mistaken for a
   * measure.
   */
  groupHints?: string[];
  /** Subset of groupHints that are DateTrunc group-bys, for temporal detection. */
  temporalHints?: string[];
}

/**
 * Derive the {@link DataShape} of a summarize result. Hints come from the query
 * plan (authoritative); the value-type heuristic is only a fallback for columns
 * not covered by hints.
 */
export function computeDataShape(
  headers: string[],
  rows: Record<string, unknown>[],
  hints: ShapeHints = {},
): DataShape {
  const { groupHints = [], temporalHints = [] } = hints;
  const { groupKeys: groupsByType } = inferColumns(headers, rows);

  // A column is a dimension if the query says so (groupHints) OR the value-type
  // heuristic classified it as one. Everything else is a measure.
  const groupSet = new Set([...groupHints.filter((h) => headers.includes(h)), ...groupsByType]);
  const groupKeys = headers.filter((h) => groupSet.has(h));
  const measureKeys = headers.filter((h) => !groupSet.has(h));

  const temporalKey = detectTemporalKey(groupKeys, rows, temporalHints);

  let temporalPoints: number | undefined;
  if (temporalKey) {
    const seen = new Set<string>();
    for (const r of rows) seen.add(String(r[temporalKey] ?? ''));
    temporalPoints = seen.size;
  }

  return {
    groupKeys,
    measureKeys,
    groupCount: groupKeys.length,
    measureCount: measureKeys.length,
    rowCount: rows.length,
    temporalKey,
    temporalPoints,
  };
}
