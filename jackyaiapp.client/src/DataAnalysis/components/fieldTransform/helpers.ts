import { DATE_CANONICAL_TYPES, NUMERIC_CANONICAL_TYPES } from '../../constants';

import type { ColumnDataType, DateGranularity } from '../../../apis/dataAnalysisApis/types';
import type { FieldTransformDef } from '../../types';

/** Add-form draft state. */
export interface DraftState {
  fieldName: string;
  name: string;
  /** Auto-derived from the field's canonicalType; not user-editable. */
  type: FieldTransformDef['type'];
  granularity: DateGranularity;
}

export const emptyDraft = (): DraftState => ({
  fieldName: '',
  name: '',
  type: 'StringGrouping',
  granularity: 'Month',
});

/** Infer the transform type from a column's canonical data type. */
export function inferTransformType(canonicalType: ColumnDataType): FieldTransformDef['type'] {
  if ((DATE_CANONICAL_TYPES as ColumnDataType[]).includes(canonicalType)) return 'DateTrunc';
  if ((NUMERIC_CANONICAL_TYPES as ColumnDataType[]).includes(canonicalType)) return 'NumericBin';
  return 'StringGrouping';
}

export const typeChipColor = (type: FieldTransformDef['type']) => {
  if (type === 'DateTrunc') return 'info' as const;
  if (type === 'NumericBin') return 'warning' as const;
  return 'secondary' as const;
};

export const summary = (t: FieldTransformDef): string => {
  if (t.type === 'DateTrunc') return t.granularity;
  if (t.type === 'NumericBin') return `${t.bins.length} bin${t.bins.length !== 1 ? 's' : ''}`;
  return `${t.groups.length} group${t.groups.length !== 1 ? 's' : ''}`;
};

/** Effective transform name, falling back to a derived label when the user left it blank. */
export const effectiveName = (d: DraftState) =>
  d.name.trim() ||
  (d.type === 'DateTrunc'
    ? `${d.fieldName} (${d.granularity})`
    : `${d.fieldName} ${d.type === 'NumericBin' ? 'Bins' : 'Groups'}`);
