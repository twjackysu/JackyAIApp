import type { FilterDimensionDto, GroupFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, ViewByEntry } from '../types';

export function makeViewByEntry(fieldName = ''): ViewByEntry {
  return {
    id: Date.now() + Math.random(),
    fieldName,
    type: 'Plain',
    alias: '',
    granularity: 'Month',
    fallback: 'Other',
    bins: [],
    groups: [],
  };
}

export function toGroupFieldDto(e: ViewByEntry): GroupFieldDto {
  return {
    type:      e.type,
    fieldName: e.fieldName,
    alias:     e.alias || null,
    granularity:
      e.type === 'DateTrunc' ? e.granularity : null,
    fallback:
      e.type === 'NumericBin' || e.type === 'StringGrouping'
        ? e.fallback || 'Other'
        : null,
    bins:
      e.type === 'NumericBin'
        ? e.bins.map((b) => ({
            min:   b.min === '' ? null : parseFloat(b.min),
            max:   b.max === '' ? null : parseFloat(b.max),
            label: b.label,
          }))
        : null,
    groups:
      e.type === 'StringGrouping'
        ? e.groups.map((g) => ({ label: g.label, values: g.values }))
        : null,
  };
}

export function toFilterDtos(filters: ActiveFilter[]): FilterDimensionDto[] {
  return filters
    .filter((f) => f.fieldName && f.values.length > 0)
    .map(({ fieldName, values }) => ({ fieldName, values }));
}
