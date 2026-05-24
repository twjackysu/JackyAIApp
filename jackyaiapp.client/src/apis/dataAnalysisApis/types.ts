// ── Enums (match C# DataAnalysisLibrary enums, serialised as strings) ──────

export type AggregationType = 'Count' | 'DistinctCount' | 'Sum' | 'Average' | 'Max' | 'Min';

export type ColumnDataType =
  | 'Unknown'
  | 'String'
  | 'Integer'
  | 'Float'
  | 'Decimal'
  | 'Boolean'
  | 'DateTime'
  | 'DateTimeOffset'
  | 'Date'
  | 'Time'
  | 'TimeSpan'
  | 'Guid'
  | 'Binary'
  | 'Json'
  | 'Xml';

// ── Schema ──────────────────────────────────────────────────────────────────

export interface FieldDefinition {
  name: string;
  dataType: string;       // native SQL type string, e.g. "nvarchar", "decimal"
  isNullable: boolean;
  canonicalType: ColumnDataType;
}

export interface FieldListResult {
  fields: FieldDefinition[];
}

// ── Summarize ───────────────────────────────────────────────────────────────

export interface MeasureFieldDto {
  fieldName: string;
  aggregation: AggregationType;
}

export interface FilterDimensionDto {
  fieldName: string;
  values: string[];
}

export interface SummarizeRequest {
  viewBy: string[];
  measures: MeasureFieldDto[];
  filters?: FilterDimensionDto[];
}

export interface SummarizeResult {
  rows: Record<string, unknown>[];
}

// ── Details ─────────────────────────────────────────────────────────────────

export interface SortFieldDto {
  fieldName: string;
  descending: boolean;
}

export interface DetailsRequest {
  selectFields?: string[];
  orderBy?: SortFieldDto[];
  skip?: number;
  take?: number;
  filters?: FilterDimensionDto[];
}

export interface DetailsResult {
  rows: Record<string, unknown>[];
  totalCount: number;
}

// ── MaxDate ──────────────────────────────────────────────────────────────────

export interface MaxDateResult {
  maxDate: string | null; // ISO-8601 DateTimeOffset string
}

// ── DistinctValues ────────────────────────────────────────────────────────────

export interface DistinctValuesResult {
  values: string[];
}
