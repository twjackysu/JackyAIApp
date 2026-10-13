import type { RowCountQueryParams, SummarizeRequest, SummarizeResult } from './types';

const COUNT_ALIAS = '_rows';

/**
 * A summarize request whose per-year counts add up to the table's row count.
 *
 * The library has no COUNT query, and a summarize without any group-by produces an empty
 * GROUP BY that SQL Server rejects. Count renders as COUNT(*) (count() on ADX), so grouping by
 * the date field's year still counts every row — rows without a date land in the NULL group —
 * while returning only a handful of rows.
 */
export function buildRowCountRequest({
  sourceId,
  tableName,
  dateField,
  filters,
}: RowCountQueryParams): SummarizeRequest {
  return {
    context: sourceId ? { sourceId, tableName } : undefined,
    viewBy: [{ type: 'DateTrunc', fieldName: dateField, granularity: 'Year', alias: '_year' }],
    measures: [{ fieldName: dateField, aggregation: 'Count', alias: COUNT_ALIAS }],
    filters: filters ?? [],
  };
}

export function sumRowCounts(result: SummarizeResult | undefined): number {
  return (result?.rows ?? []).reduce((total, row) => total + Number(row[COUNT_ALIAS] ?? 0), 0);
}
