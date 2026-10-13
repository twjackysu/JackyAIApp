import { useEffect, useState } from 'react';

import { useDetailsMutation, useGetRowCountQuery } from '../../apis/dataAnalysisApis';
import { ROWS_PER_PAGE } from '../constants';
import { toFilterDtos } from '../utils/groupField';

import type { ActiveFilter } from '../types';

interface UseDetailsParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  tab: number;
  dateField: string;
  /** The date field when it really is a date column, which the row count needs; else undefined. */
  rowCountField?: string;
  sessionId?: string;
}

export function useDetails({
  ctxSourceId,
  ctxTableName,
  tab,
  dateField,
  rowCountField,
  sessionId,
}: UseDetailsParams) {
  const [page, setPage] = useState(0);
  const [detailsFilters, setDetailsFilters] = useState<ActiveFilter[]>([]);

  const [details, { data: detailsData, isLoading: detailsLoading, error: detailsError }] =
    useDetailsMutation();

  // Auto-load whenever the tab is active or any dependency changes. Only the page on screen is
  // fetched; the total comes from the count below, not from pulling more rows.
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

  // Database sources: the details response's totalCount is only the page size, so count the
  // filtered rows separately. Cached per filter set, so paging doesn't refetch it.
  const { data: dbTotal } = useGetRowCountQuery(
    {
      sourceId: ctxSourceId,
      tableName: ctxTableName,
      dateField: rowCountField ?? '',
      filters: toFilterDtos(detailsFilters, []),
    },
    { skip: tab !== 2 || !!sessionId || !rowCountField },
  );

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
  // Session uploads return the real filtered total. Without a count (no date column, or still
  // loading) a full page means there may be more: -1 makes TablePagination show "more than N".
  const knownTotal = sessionId ? detailsData?.data?.totalCount : dbTotal;
  const detailsTotalCount =
    knownTotal ??
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
