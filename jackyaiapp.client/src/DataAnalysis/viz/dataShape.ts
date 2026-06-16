import { inferColumns } from '../components/summarizeChart/dataPrep';

import type { DataShape } from './types';

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

/**
 * Derive the {@link DataShape} of a summarize result. `temporalHints` should be
 * the output column names (alias ?? fieldName) of any DateTrunc group-bys in the
 * query plan, for precise temporal detection; the heuristic is the fallback.
 */
export function computeDataShape(
  headers: string[],
  rows: Record<string, unknown>[],
  temporalHints: string[] = [],
): DataShape {
  const { groupKeys, measureKeys } = inferColumns(headers, rows);
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
