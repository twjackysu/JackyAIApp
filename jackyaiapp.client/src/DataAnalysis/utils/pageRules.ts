import { DATE_CANONICAL_TYPES, DEFAULT_TABLE } from '../constants';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';

/** The table a source opens on: the built-in source's default table, or the source's own default
 * table ('' when it has none). `sourceId` null is the built-in source. */
export function tableForSource(
  sources: readonly { id: string; defaultTableName?: string | null }[],
  sourceId: string | null,
): string {
  if (sourceId === null) return DEFAULT_TABLE;
  return sources.find((s) => s.id === sourceId)?.defaultTableName ?? '';
}

/** Row counts group by the date field's year, so they need it to be a real date column. */
export function rowCountFieldOf(fields: FieldDefinition[], dateField: string): string | undefined {
  return fields.some((f) => f.name === dateField && DATE_CANONICAL_TYPES.includes(f.canonicalType))
    ? dateField
    : undefined;
}
