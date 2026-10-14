import type { FieldTransformDef } from './types';
import type {
  AggregationType,
  ColumnDataType,
  DateGranularity,
} from '../apis/dataAnalysisApis/types';

export const DATE_CANONICAL_TYPES: ColumnDataType[] = ['DateTime', 'DateTimeOffset', 'Date'];
export const NUMERIC_CANONICAL_TYPES: ColumnDataType[] = ['Integer', 'Float', 'Decimal'];

export const AGGREGATION_TYPES: AggregationType[] = [
  'Count',
  'DistinctCount',
  'Sum',
  'Average',
  'Max',
  'Min',
];

export const DATE_GRANULARITIES: DateGranularity[] = [
  'Year',
  'Quarter',
  'Month',
  'Week',
  'Day',
  'Hour',
];

/** Labels for FieldTransformDef.type shown in chips and selectors. */
export const TRANSFORM_TYPE_LABELS: Record<FieldTransformDef['type'], string> = {
  DateTrunc: '日期分組 (DateTrunc)',
  NumericBin: '數值分組 (NumericBin)',
  StringGrouping: '文字分組 (StringGrouping)',
};

export const ROWS_PER_PAGE = 50;
export const DEFAULT_TABLE = 'dbo.Orders';

/**
 * Disclosure under the 「問 AI」 input (TASK-052 / DECISION-028). Keep it in step with what
 * `DataAnalysisController.BuildNlPlanAsync` puts in the OpenAI prompt: the question, every column
 * name, the listed values of columns with few distinct values, each date column's latest date, and —
 * on the one retry after a filter value was not found — similar real values searched from the data
 * (including columns too large to list up front). It is a notice, not a promise about retention.
 */
export const AI_DATA_NOTICE =
  '提問時，會把你的問題、這份資料的欄位名稱、各欄位可選的值（例如地區、產品類別）和最新日期傳送給 AI 服務（OpenAI）來理解問題。若問題中的某個值在資料裡找不到，會再附上資料中相近的值重問一次。';
