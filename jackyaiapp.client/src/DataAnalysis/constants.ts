import type { AggregationType, ColumnDataType, DateGranularity } from '../apis/dataAnalysisApis/types';
import type { FieldTransformDef } from './types';

export const DATE_CANONICAL_TYPES: ColumnDataType[]    = ['DateTime', 'DateTimeOffset', 'Date'];
export const NUMERIC_CANONICAL_TYPES: ColumnDataType[] = ['Integer', 'Float', 'Decimal'];

export const AGGREGATION_TYPES: AggregationType[] = [
  'Count', 'DistinctCount', 'Sum', 'Average', 'Max', 'Min',
];

export const DATE_GRANULARITIES: DateGranularity[] = [
  'Year', 'Quarter', 'Month', 'Week', 'Day', 'Hour',
];

/** Labels for FieldTransformDef.type shown in chips and selectors. */
export const TRANSFORM_TYPE_LABELS: Record<FieldTransformDef['type'], string> = {
  DateTrunc:      'Date Trunc',
  NumericBin:     'Numeric Bins',
  StringGrouping: 'String Groups',
};

export const ROWS_PER_PAGE = 50;
export const DEFAULT_TABLE = 'dbo.Orders';
