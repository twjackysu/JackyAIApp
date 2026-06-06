import { useEffect, useMemo, useState } from 'react';

import { useGetSourcesQuery } from '../../apis/dataAnalysisApis';
import { DEFAULT_TABLE } from '../constants';

import type { SessionInfo } from '../../apis/dataAnalysisApis/types';

export function useSourceContext(isLoggedIn: boolean) {
  const [activeSourceId, setActiveSourceId] = useState<string | null>(null);
  const [activeTableName, setActiveTableName] = useState(DEFAULT_TABLE);
  const [tableNameInput, setTableNameInput] = useState(DEFAULT_TABLE);
  const [managerOpen, setManagerOpen] = useState(false);
  const [sessionInfo, setSessionInfo] = useState<SessionInfo | null>(null);
  const [uploadOpen, setUploadOpen] = useState(false);

  const { data: sourcesRes } = useGetSourcesQuery(undefined, { skip: !isLoggedIn });
  // Stabilize the reference so it can be a safe useEffect dependency.
  const sources = useMemo(() => sourcesRes?.data ?? [], [sourcesRes]);

  // If the active source is deleted externally, revert to default
  useEffect(() => {
    if (activeSourceId && sources.length > 0 && !sources.some((s) => s.id === activeSourceId)) {
      setActiveSourceId(null);
      setTableNameInput(DEFAULT_TABLE);
      setActiveTableName(DEFAULT_TABLE);
    }
  }, [sources, activeSourceId]);

  const applyTableName = () => setActiveTableName(tableNameInput);

  const clearSession = () => setSessionInfo(null);

  const ctxSourceId = activeSourceId ?? undefined;
  const ctxTableName = activeTableName || undefined;
  const activeSource = sources.find((s) => s.id === activeSourceId);

  return {
    activeSourceId,
    setActiveSourceId,
    activeTableName,
    setActiveTableName,
    tableNameInput,
    setTableNameInput,
    managerOpen,
    setManagerOpen,
    sources,
    activeSource,
    ctxSourceId,
    ctxTableName,
    applyTableName,
    sessionInfo,
    setSessionInfo,
    clearSession,
    uploadOpen,
    setUploadOpen,
  };
}
