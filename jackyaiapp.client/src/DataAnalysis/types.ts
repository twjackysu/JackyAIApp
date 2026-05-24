import type { DateGranularity } from '../apis/dataAnalysisApis/types';

// ── Filter ────────────────────────────────────────────────────────────────────

export interface ActiveFilter {
  id: number;
  fieldName: string;
  values: string[];
  /** When true, `values` contains transform group/bin labels rather than raw field values. */
  useTransform: boolean;
  /** References a FieldTransformDef.id when useTransform is true. */
  transformId: number | null;
}

// ── Transform building blocks ─────────────────────────────────────────────────

export interface BinEntry {
  id: number;
  min: string;
  max: string;
  label: string;
}

export interface GroupEntry {
  id: number;
  label: string;
  values: string[];
}

// ── Standalone field-value transform ──────────────────────────────────────────

/**
 * Defines how a field's values should be transformed.
 * Stored independently so both ViewBy and Filters can reference the same definition.
 *
 * - DateTrunc      → truncate datetime to a calendar period (needs `granularity`)
 * - NumericBin     → bucket numeric values into labeled ranges (needs `bins`)
 * - StringGrouping → map raw strings to logical groups (needs `groups`)
 */
export interface FieldTransformDef {
  id: number;
  fieldName: string;
  /** User-facing name shown in selectors, e.g. "Order Month" or "Age Groups". */
  name: string;
  type: 'DateTrunc' | 'NumericBin' | 'StringGrouping';
  granularity: DateGranularity;  // DateTrunc
  bins: BinEntry[];              // NumericBin
  groups: GroupEntry[];          // StringGrouping
  fallback: string;              // NumericBin / StringGrouping fallback label
}

// ── ViewBy ────────────────────────────────────────────────────────────────────

export interface ViewByEntry {
  id: number;
  fieldName: string;
  /**
   * - Plain     → raw column value, no transformation
   * - Transform → apply a FieldTransformDef (DateTrunc / NumericBin / StringGrouping)
   */
  type: 'Plain' | 'Transform';
  alias: string;
  transformId: number | null;   // used when type === 'Transform'
}
