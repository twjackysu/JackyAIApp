import type { FilterDimensionDto, GroupFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';

export function makeViewByEntry(fieldName = ''): ViewByEntry {
  return {
    id: Date.now() + Math.random(),
    fieldName,
    type: 'Plain',
    alias: '',
    transformId: null,
  };
}

/**
 * Convert a ViewByEntry to the DTO the server expects.
 * When type === 'Transform', resolves the referenced FieldTransformDef.
 * Falls back to Plain if the transform is missing or the entry is misconfigured.
 */
export function toGroupFieldDto(
  e: ViewByEntry,
  transforms: FieldTransformDef[],
): GroupFieldDto {
  if (e.type === 'Transform' && e.transformId !== null) {
    const t = transforms.find((t) => t.id === e.transformId);
    if (t) {
      return {
        type:       t.type,
        fieldName:  e.fieldName,
        alias:      e.alias || null,
        granularity: t.type === 'DateTrunc' ? t.granularity : null,
        fallback:
          t.type === 'NumericBin' || t.type === 'StringGrouping'
            ? t.fallback || 'Other'
            : null,
        bins:
          t.type === 'NumericBin'
            ? t.bins.map((b) => ({
                min:   b.min === '' ? null : parseFloat(b.min),
                max:   b.max === '' ? null : parseFloat(b.max),
                label: b.label,
              }))
            : null,
        groups:
          t.type === 'StringGrouping'
            ? t.groups.map((g) => ({ label: g.label, values: g.values }))
            : null,
      };
    }
  }

  // Plain (also the fallback when a Transform reference is broken)
  return {
    type:       'Plain',
    fieldName:  e.fieldName,
    alias:      e.alias || null,
    granularity: null,
    fallback:   null,
    bins:       null,
    groups:     null,
  };
}

/**
 * Convert ActiveFilters to server DTOs.
 * StringGrouping transforms: expand selected group labels → constituent raw values.
 * NumericBin and DateTrunc transforms are not supported in filters.
 */
export function toFilterDtos(
  filters: ActiveFilter[],
  transforms: FieldTransformDef[],
): FilterDimensionDto[] {
  return filters
    .filter((f) => f.fieldName && f.values.length > 0)
    .map((f) => {
      if (f.useTransform && f.transformId !== null) {
        const t = transforms.find((t) => t.id === f.transformId);
        if (t?.type === 'StringGrouping') {
          const rawValues = f.values.flatMap((label) => {
            const grp = t.groups.find((g) => g.label === label);
            return grp ? grp.values : [label];
          });
          if (rawValues.length > 0) {
            return { fieldName: f.fieldName, values: rawValues };
          }
        }
      }
      return { fieldName: f.fieldName, values: f.values };
    });
}
