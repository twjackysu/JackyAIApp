import { useState } from 'react';
import { useSummarizeMutation } from '../../apis/dataAnalysisApis';
import type { FieldDefinition, MeasureFieldDto } from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, ViewByEntry } from '../types';
import { makeViewByEntry, toFilterDtos, toGroupFieldDto } from '../utils/groupField';

interface UseSummarizeParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  fields: FieldDefinition[];
}

export function useSummarize({ ctxSourceId, ctxTableName, fields }: UseSummarizeParams) {
  const [viewByEntries, setViewByEntries] = useState<ViewByEntry[]>([
    makeViewByEntry('Region'),
  ]);
  const [measures, setMeasures] = useState<MeasureFieldDto[]>([
    { fieldName: 'Revenue', aggregation: 'Sum' },
  ]);
  const [summarizeFilters, setSummarizeFilters] = useState<ActiveFilter[]>([]);

  const [summarize, { data: summarizeData, isLoading: summarizeLoading, error: summarizeError }] =
    useSummarizeMutation();

  const handleRunSummarize = () => {
    if (viewByEntries.length === 0 || measures.length === 0) return;
    summarize({
      context: ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      viewBy:  viewByEntries.map(toGroupFieldDto),
      measures,
      filters: toFilterDtos(summarizeFilters),
    });
  };

  const addMeasure = () =>
    setMeasures((prev) => [
      ...prev,
      { fieldName: fields[0]?.name ?? '', aggregation: 'Sum' },
    ]);

  const removeMeasure = (i: number) =>
    setMeasures((prev) => prev.filter((_, idx) => idx !== i));

  const updateMeasure = (i: number, patch: Partial<MeasureFieldDto>) =>
    setMeasures((prev) => prev.map((m, idx) => (idx === i ? { ...m, ...patch } : m)));

  const reset = () => {
    setViewByEntries([]);
    setMeasures([{ fieldName: '', aggregation: 'Sum' }]);
    setSummarizeFilters([]);
  };

  const summarizeRows    = summarizeData?.data?.rows ?? [];
  const summarizeHeaders = summarizeRows.length > 0 ? Object.keys(summarizeRows[0]) : [];

  return {
    viewByEntries,    setViewByEntries,
    measures,         addMeasure, removeMeasure, updateMeasure,
    summarizeFilters, setSummarizeFilters,
    handleRunSummarize,
    summarizeLoading, summarizeError,
    summarizeRows,    summarizeHeaders,
    reset,
  };
}
