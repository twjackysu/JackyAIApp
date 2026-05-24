import type { AggregationType, ColumnDataType, DateGranularity, GroupFieldType } from '../apis/dataAnalysisApis/types';

export const DATE_CANONICAL_TYPES: ColumnDataType[] = ['DateTime', 'DateTimeOffset', 'Date'];

export const AGGREGATION_TYPES: AggregationType[] = [
  'Count', 'DistinctCount', 'Sum', 'Average', 'Max', 'Min',
];

export const DATE_GRANULARITIES: DateGranularity[] = [
  'Year', 'Quarter', 'Month', 'Week', 'Day', 'Hour',
];

export const GROUP_FIELD_TYPE_LABELS: Record<GroupFieldType, string> = {
  Plain:          'Plain',
  DateTrunc:      'Date Trunc',
  NumericBin:     'Numeric Bins',
  StringGrouping: 'String Groups',
};

export const ROWS_PER_PAGE = 50;
export const DEFAULT_TABLE = 'dbo.Orders';
