import type { ColumnDataType } from '../../apis/dataAnalysisApis/types';

export function formatCellValue(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'string') {
    if (/^\d{4}-\d{2}-\d{2}T/.test(value)) return value.substring(0, 10);
    return value;
  }
  if (typeof value === 'number')
    return Number.isInteger(value) ? String(value) : value.toFixed(2);
  return String(value);
}

export function canonicalTypeColor(
  type: ColumnDataType,
): 'default' | 'primary' | 'secondary' | 'success' | 'warning' | 'info' {
  switch (type) {
    case 'Integer':
    case 'Float':
    case 'Decimal':
      return 'primary';
    case 'Boolean':
      return 'warning';
    case 'DateTime':
    case 'DateTimeOffset':
    case 'Date':
    case 'Time':
      return 'success';
    case 'Guid':
      return 'info';
    default:
      return 'default';
  }
}
