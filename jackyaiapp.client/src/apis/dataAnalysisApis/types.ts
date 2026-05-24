// ── Enums (match C# DataAnalysisLibrary enums, serialised as strings) ──────

export type AggregationType = 'Count' | 'DistinctCount' | 'Sum' | 'Average' | 'Max' | 'Min';

export type GroupFieldType = 'Plain' | 'DateTrunc' | 'NumericBin' | 'StringGrouping';
export type DateGranularity = 'Year' | 'Quarter' | 'Month' | 'Week' | 'Day' | 'Hour';

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

// ── Data-source management ─────────────────────────────────────────────────

export type DataSourceConnectorType = 'MsSql' | 'Adx';

/** Optional query context — null/omitted = use the default JackyAI SQL source. */
export interface QueryContextDto {
  sourceId?: string;
  tableName?: string;
}

export interface MsSqlSourceConfig {
  connectionString: string;
}

export interface AdxSourceConfig {
  clusterUri: string;
  database: string;
  tenantId?: string | null;
  clientId?: string | null;
  /** Returned as "***" when masked; send "***" back to keep existing secret. */
  clientSecret?: string | null;
  /** Optional raw ADX connection string that overrides all SP fields above. */
  connectionString?: string | null;
}

export interface DataSourceConfigDto {
  id: string;
  name: string;
  connectorType: DataSourceConnectorType;
  /** Secrets are masked ("***") in this field. */
  config: Record<string, unknown>;
  defaultTableName?: string | null;
  createdAt: string;
}

export interface SaveDataSourceConfigDto {
  name: string;
  connectorType: DataSourceConnectorType;
  config: MsSqlSourceConfig | AdxSourceConfig;
  defaultTableName?: string | null;
}

// ── Shared query-param shapes (GET endpoints) ──────────────────────────────

export interface SourceQueryParams {
  sourceId?: string;
  tableName?: string;
}

export interface MaxDateQueryParams extends SourceQueryParams {
  /** The date column to compute MAX on. Defaults to "OrderDate" server-side if omitted. */
  dateField?: string;
}

export interface DistinctValuesQueryParams extends SourceQueryParams {
  field: string;
}

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

// ── GroupField DTOs ──────────────────────────────────────────────────────────

export interface NumericBinDto {
  min: number | null;
  max: number | null;
  label: string;
}

export interface StringGroupDto {
  label: string;
  values: string[];
}

/** Discriminated-union DTO for all GroupFieldBase subtypes. */
export interface GroupFieldDto {
  type: GroupFieldType;
  fieldName: string;
  alias?: string | null;
  /** DateTrunc only */
  granularity?: DateGranularity | null;
  /** NumericBin / StringGrouping only */
  fallback?: string | null;
  /** NumericBin only */
  bins?: NumericBinDto[] | null;
  /** StringGrouping only */
  groups?: StringGroupDto[] | null;
}

// ── Summarize ───────────────────────────────────────────────────────────────

export interface MeasureFieldDto {
  fieldName: string;
  aggregation: AggregationType;
  /** Output column alias. Defaults to fieldName when omitted. */
  alias?: string;
}

export interface FilterDimensionDto {
  fieldName: string;
  values: string[];
  /**
   * Filter operator — matches C# FilterOperator enum (serialised as string).
   * "In" | "NotIn" | "Equals" | "GreaterThan" | "LessThan" | "Between" | "Contains"
   * Defaults to "In" when omitted.
   */
  op?: string;
}

export interface SummarizeRequest {
  context?: QueryContextDto;
  viewBy: GroupFieldDto[];
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
  context?: QueryContextDto;
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
