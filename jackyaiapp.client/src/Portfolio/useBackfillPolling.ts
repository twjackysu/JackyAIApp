import { useEffect } from 'react';

import type { HistoryBackfill } from '@/apis/portfolioApis/types';

/**
 * How often a chart asks again while the server is still fetching older prices. Every request
 * reads the stored prices for the whole window (3 years × every held stock) from a 5 DTU
 * database, so this stays well above 10 seconds.
 */
export const BACKFILL_POLL_INTERVAL_MS = 15_000;

/**
 * The RTK Query `pollingInterval` for a response's backfill status: polls only while `Running`.
 * `Stalled` is deliberately not polled — nothing is working on it and the server does not requeue
 * for about 10 minutes, so asking again would only cost database reads; a page refresh retries.
 */
export function backfillPollingInterval(backfill: HistoryBackfill | null | undefined): number {
  return backfill?.state === 'Running' ? BACKFILL_POLL_INTERVAL_MS : 0;
}

/**
 * Keeps a chart's `pollingInterval` in step with the backfill status of its own response. The
 * interval is passed to the query hook that returns that response, so it has to live in state the
 * chart declares first:
 *
 *   const [pollingInterval, setPollingInterval] = useState(0);
 *   const { currentData } = useGetXQuery(arg, { pollingInterval, skipPollingIfUnfocused: true });
 *   useBackfillPolling(currentData?.data.backfill, setPollingInterval);
 */
export function useBackfillPolling(
  backfill: HistoryBackfill | null | undefined,
  setPollingInterval: (ms: number) => void,
) {
  const interval = backfillPollingInterval(backfill);
  useEffect(() => {
    setPollingInterval(interval);
  }, [interval, setPollingInterval]);
}
