import { useEffect, useState } from 'react';

import { useDetailsMutation } from '../../apis/dataAnalysisApis';
import { ROWS_PER_PAGE } from '../constants';
import { toFilterDtos } from '../utils/groupField';

import type { ActiveFilter } from '../types';

interface UseDetailsParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  tab: number;
  dateField: string;
  sessionId?: string;
}

export function useDetails({
  ctxSourceId,
  ctxTableName,
  tab,
  dateField,
  sessionId,
}: UseDetailsParams) {
  const [page, setPage] = useState(0);
  const [detailsFilters, setDetailsFilters] = useState<ActiveFilter[]>([]);

  const [details, { data: detailsData, isLoading: detailsLoading, error: detailsError }] =
    useDetailsMutation();

  // Auto-load whenever the tab is active or any dependency changes
  useEffect(() => {
    if (tab !== 2) return;
    details({
      sessionId,
      context:
        !sessionId && ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      orderBy: dateField ? [{ fieldName: dateField, descending: true }] : [],
      skip: page * ROWS_PER_PAGE,
      // +1 row to detect whether a next page exists — the library's TotalCount
      // is just the returned page size, not a real COUNT over the table.
      take: ROWS_PER_PAGE + 1,
      filters: toFilterDtos(detailsFilters, []), // Details tab has no field transforms
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, page, detailsFilters, ctxSourceId, ctxTableName, dateField, sessionId]);

  const handleDetailsFiltersChange = (newFilters: ActiveFilter[]) => {
    setDetailsFilters(newFilters);
    setPage(0);
  };

  const reset = () => {
    setDetailsFilters([]);
    setPage(0);
  };

  const fetchedRows = detailsData?.data?.rows ?? [];
  const hasMore = fetchedRows.length > ROWS_PER_PAGE;
  const detailsRows = hasMore ? fetchedRows.slice(0, ROWS_PER_PAGE) : fetchedRows;
  const detailsHeaders = detailsRows.length > 0 ? Object.keys(detailsRows[0]) : [];
  // -1 tells TablePagination the total is unknown ("more than N"); on the last
  // page we can compute the exact total from what came back.
  const detailsTotalCount = hasMore ? -1 : page * ROWS_PER_PAGE + fetchedRows.length;

  return {
    page,
    setPage,
    detailsFilters,
    handleDetailsFiltersChange,
    detailsLoading,
    detailsError,
    detailsRows,
    detailsHeaders,
    detailsTotalCount,
    reset,
  };
}
