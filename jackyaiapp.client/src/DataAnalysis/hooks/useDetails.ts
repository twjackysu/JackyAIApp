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

  // Auto-load whenever the tab is active or any dependency changes. Only the page on screen is
  // fetched; the response's totalCount is the filtered total, not the page size.
  useEffect(() => {
    if (tab !== 2) return;
    details({
      sessionId,
      context:
        !sessionId && ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      orderBy: dateField ? [{ fieldName: dateField, descending: true }] : [],
      skip: page * ROWS_PER_PAGE,
      take: ROWS_PER_PAGE,
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

  const detailsRows = detailsData?.data?.rows ?? [];
  const detailsHeaders = detailsRows.length > 0 ? Object.keys(detailsRows[0]) : [];
  // Both database sources (DataAnalysisLibrary >= 1.1) and session uploads return the real
  // filtered total. Until it arrives, a full page means there may be more: -1 makes
  // TablePagination show "more than N".
  const detailsTotalCount =
    detailsData?.data?.totalCount ??
    (detailsRows.length === ROWS_PER_PAGE ? -1 : page * ROWS_PER_PAGE + detailsRows.length);

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
