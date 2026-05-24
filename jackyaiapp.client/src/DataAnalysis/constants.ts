import type { AggregationType, ColumnDataType, DateGranularity } from '../apis/dataAnalysisApis/types';
import type { FieldTransformDef, ViewByEntry } from './types';

export const DATE_CANONICAL_TYPES: ColumnDataType[]    = ['DateTime', 'DateTimeOffset', 'Date'];
export const NUMERIC_CANONICAL_TYPES: ColumnDataType[] = ['Integer', 'Float', 'Decimal'];

export const AGGREGATION_TYPES: AggregationType[] = [
  'Count', 'DistinctCount', 'Sum', 'Average', 'Max', 'Min',
];

export const DATE_GRANULARITIES: DateGranularity[] = [
  'Year', 'Quarter', 'Month', 'Week', 'Day', 'Hour',
];

/** Labels for ViewByEntry.type shown in the Group By type selector. */
export const VIEW_BY_TYPE_LABELS: Record<ViewByEntry['type'], string> = {
  Plain:     'Plain',
  Transform: 'Apply Transform',
};

/** Labels for FieldTransformDef.type shown in chips and selectors. */
export const TRANSFORM_TYPE_LABELS: Record<FieldTransformDef['type'], string> = {
  DateTrunc:      'Date Trunc',
  NumericBin:     'Numeric Bins',
  StringGrouping: 'String Groups',
};

export const ROWS_PER_PAGE = 50;
export const DEFAULT_TABLE = 'dbo.Orders';
