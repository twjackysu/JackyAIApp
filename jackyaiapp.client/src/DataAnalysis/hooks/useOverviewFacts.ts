import { useGetMaxDateQuery, useGetRowCountQuery } from '../../apis/dataAnalysisApis';
import { rowCountFieldOf } from '../utils/pageRules';

import type { FieldDefinition, SessionInfo } from '../../apis/dataAnalysisApis/types';

import { fmtNum } from '@/utils/format';

interface UseOverviewFactsParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  fields: FieldDefinition[];
  dateField: string;
  sessionInfo: SessionInfo | null | undefined;
}

/** The row count and latest date shown over the tabs. */
export function useOverviewFacts({
  ctxSourceId,
  ctxTableName,
  fields,
  dateField,
  sessionInfo,
}: UseOverviewFactsParams) {
  const sessionId = sessionInfo?.sessionId;

  const { data: maxDateData, isLoading: maxDateLoading } = useGetMaxDateQuery({
    sessionId,
    sourceId: ctxSourceId,
    tableName: ctxTableName,
    dateField,
  });

  // Session uploads already know their row count. DB sources get an exact count from one
  // aggregate query; the same query (no filters) backs the Details tab's total, so it's cached.
  const rowCountField = rowCountFieldOf(fields, dateField);
  const { data: dbRowCount, isFetching: rowCountFetching } = useGetRowCountQuery(
    { sourceId: ctxSourceId, tableName: ctxTableName, dateField: rowCountField ?? '', filters: [] },
    { skip: !!sessionId || !rowCountField },
  );
  const rowCountDisplay = sessionId
    ? sessionInfo && fmtNum(sessionInfo.rowCount)
    : dbRowCount == null
      ? undefined
      : fmtNum(dbRowCount);

  return {
    rowCountDisplay,
    rowCountLoading: !sessionId && rowCountFetching,
    maxDate: maxDateData?.data?.maxDate,
    maxDateLoading,
  };
}
