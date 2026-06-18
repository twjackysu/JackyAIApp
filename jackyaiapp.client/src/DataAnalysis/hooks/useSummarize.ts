import { useState } from 'react';

import { useSummarizeMutation } from '../../apis/dataAnalysisApis';
import { makeViewByEntry, toFilterDtos, toGroupFieldDto } from '../utils/groupField';

import type {
  FieldDefinition,
  MeasureFieldDto,
  SummarizeRequest,
} from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';

interface UseSummarizeParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  fields: FieldDefinition[];
  sessionId?: string;
}

export function useSummarize({ ctxSourceId, ctxTableName, fields, sessionId }: UseSummarizeParams) {
  // ── Field Transforms (shared by ViewBy and Filters) ───────────────────────
  const [fieldTransforms, setFieldTransforms] = useState<FieldTransformDef[]>([]);

  // ── ViewBy / Measures / Filters ───────────────────────────────────────────
  const [viewByEntries, setViewByEntries] = useState<ViewByEntry[]>([makeViewByEntry('Region')]);
  const [measures, setMeasures] = useState<MeasureFieldDto[]>([
    { fieldName: 'Revenue', aggregation: 'Sum' },
  ]);
  const [summarizeFilters, setSummarizeFilters] = useState<ActiveFilter[]>([]);

  const [summarize, { data: summarizeData, isLoading: summarizeLoading, error: summarizeError }] =
    useSummarizeMutation();

  // Bumped each time a new manual result arrives, so the result view can be
  // remounted (keyed) to reset its per-result UI state.
  const [resultVersion, setResultVersion] = useState(0);

  // The exact re-runnable plan (no sessionId) behind the current result, for
  // "Add to dashboard". Null until a query runs / when the result came from a
  // session (sessions are ephemeral and can't be saved).
  const [lastRequest, setLastRequest] = useState<SummarizeRequest | null>(null);

  // ── Run ───────────────────────────────────────────────────────────────────
  const handleRunSummarize = async () => {
    if (viewByEntries.length === 0 || measures.length === 0) return;
    const plan: SummarizeRequest = {
      context:
        !sessionId && ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      viewBy: viewByEntries.map((e) => toGroupFieldDto(e, fieldTransforms)),
      measures,
      filters: toFilterDtos(summarizeFilters, fieldTransforms),
    };
    try {
      await summarize({ sessionId, ...plan }).unwrap();
      setLastRequest(sessionId ? null : plan);
      setResultVersion((v) => v + 1);
    } catch {
      // Error is surfaced via summarizeError.
    }
  };

  // ── FieldTransform CRUD ───────────────────────────────────────────────────
  const addTransform = (t: Omit<FieldTransformDef, 'id'>) =>
    setFieldTransforms((prev) => [...prev, { ...t, id: Date.now() + Math.random() }]);

  const updateTransform = (updated: FieldTransformDef) =>
    setFieldTransforms((prev) => prev.map((t) => (t.id === updated.id ? updated : t)));

  const removeTransform = (id: number) => {
    setFieldTransforms((prev) => prev.filter((t) => t.id !== id));
    // Reset ViewBy entries referencing this transform → Plain
    setViewByEntries((prev) =>
      prev.map((e) => (e.transformId === id ? { ...e, type: 'Plain', transformId: null } : e)),
    );
    // Reset filter entries referencing this transform → raw-value mode
    setSummarizeFilters((prev) =>
      prev.map((f) =>
        f.transformId === id ? { ...f, useTransform: false, transformId: null, values: [] } : f,
      ),
    );
  };

  // ── Measure CRUD ──────────────────────────────────────────────────────────
  const addMeasure = () =>
    setMeasures((prev) => [...prev, { fieldName: fields[0]?.name ?? '', aggregation: 'Sum' }]);

  const removeMeasure = (i: number) => setMeasures((prev) => prev.filter((_, idx) => idx !== i));

  const updateMeasure = (i: number, patch: Partial<MeasureFieldDto>) =>
    setMeasures((prev) => prev.map((m, idx) => (idx === i ? { ...m, ...patch } : m)));

  // ── Reset (source change) ─────────────────────────────────────────────────
  const reset = () => {
    setFieldTransforms([]);
    setViewByEntries([]);
    setMeasures([{ fieldName: '', aggregation: 'Sum' }]);
    setSummarizeFilters([]);
  };

  const summarizeRows = summarizeData?.data?.rows ?? [];
  const summarizeHeaders = summarizeRows.length > 0 ? Object.keys(summarizeRows[0]) : [];

  return {
    fieldTransforms,
    addTransform,
    updateTransform,
    removeTransform,
    viewByEntries,
    setViewByEntries,
    measures,
    addMeasure,
    removeMeasure,
    updateMeasure,
    summarizeFilters,
    setSummarizeFilters,
    handleRunSummarize,
    summarizeLoading,
    summarizeError,
    summarizeRows,
    summarizeHeaders,
    resultVersion,
    lastRequest,
    reset,
  };
}
