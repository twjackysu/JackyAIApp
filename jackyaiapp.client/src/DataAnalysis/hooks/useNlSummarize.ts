import { useState } from 'react';

import { useNlSummarizeMutation } from '../../apis/dataAnalysisApis';

import type { NlSummarizeResult } from '../../apis/dataAnalysisApis/types';

interface UseNlSummarizeParams {
  ctxSourceId?: string;
  ctxTableName?: string;
  sessionId?: string;
}

/** 問 AI: the question asked of the source in use (or the upload), and its latest answer. */
export function useNlSummarize({ ctxSourceId, ctxTableName, sessionId }: UseNlSummarizeParams) {
  const [nlSummarize, { isLoading, error }] = useNlSummarizeMutation();
  const [result, setResult] = useState<NlSummarizeResult | null>(null);
  // Bumped when a new AI result arrives, so the result view can be remounted
  // (keyed) to reset its per-result UI state.
  const [version, setVersion] = useState(0);

  const run = async (query: string) => {
    setResult(null);
    const res = await nlSummarize({
      context: ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      sessionId,
      query,
    });
    if ('data' in res && res.data?.data) {
      setResult(res.data.data);
      setVersion((v) => v + 1);
    }
  };

  return { result, version, isLoading, error, run, clear: () => setResult(null) };
}
