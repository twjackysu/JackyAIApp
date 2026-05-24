import { useEffect, useState } from 'react';
import { useDetailsMutation } from '../../apis/dataAnalysisApis';
import { ROWS_PER_PAGE } from '../constants';
import type { ActiveFilter } from '../types';
import { toFilterDtos } from '../utils/groupField';

interface UseDetailsParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  tab: number;
  dateField: string;
}

export function useDetails({ ctxSourceId, ctxTableName, tab, dateField }: UseDetailsParams) {
  const [page, setPage]                     = useState(0);
  const [detailsFilters, setDetailsFilters] = useState<ActiveFilter[]>([]);

  const [details, { data: detailsData, isLoading: detailsLoading, error: detailsError }] =
    useDetailsMutation();

  // Auto-load whenever the tab is active or any dependency changes
  useEffect(() => {
    if (tab !== 2) return;
    details({
      context: ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      orderBy: dateField ? [{ fieldName: dateField, descending: true }] : [],
      skip:    page * ROWS_PER_PAGE,
      take:    ROWS_PER_PAGE,
      filters: toFilterDtos(detailsFilters, []),  // Details tab has no field transforms
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, page, detailsFilters, ctxSourceId, ctxTableName, dateField]);

  const handleDetailsFiltersChange = (newFilters: ActiveFilter[]) => {
    setDetailsFilters(newFilters);
    setPage(0);
  };

  const reset = () => {
    setDetailsFilters([]);
    setPage(0);
  };

  const detailsRows       = detailsData?.data?.rows ?? [];
  const detailsHeaders    = detailsRows.length > 0 ? Object.keys(detailsRows[0]) : [];
  const detailsTotalCount = detailsData?.data?.totalCount ?? 0;

  return {
    page,          setPage,
    detailsFilters, handleDetailsFiltersChange,
    detailsLoading, detailsError,
    detailsRows,    detailsHeaders, detailsTotalCount,
    reset,
  };
}
