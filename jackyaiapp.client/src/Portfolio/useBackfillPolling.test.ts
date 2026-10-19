import { describe, expect, it } from 'vitest';

import { BACKFILL_POLL_INTERVAL_MS, backfillPollingInterval } from './useBackfillPolling';

import type { HistoryBackfill } from '@/apis/portfolioApis/types';

const status = (state: HistoryBackfill['state']): HistoryBackfill => ({
  state,
  coveredFrom: '2026-02-03',
  targetFrom: '2025-10-19',
});

describe('backfillPollingInterval', () => {
  it('polls only while the server is still fetching', () => {
    expect(backfillPollingInterval(status('Running'))).toBe(BACKFILL_POLL_INTERVAL_MS);
  });

  it('does not poll a stalled backfill — nothing is working on it and it is not requeued soon', () => {
    expect(backfillPollingInterval(status('Stalled'))).toBe(0);
  });

  it('does not poll a complete backfill or a response without one', () => {
    expect(backfillPollingInterval(status('Complete'))).toBe(0);
    expect(backfillPollingInterval(null)).toBe(0);
    expect(backfillPollingInterval(undefined)).toBe(0);
  });

  it('keeps well clear of hammering the 5 DTU database', () => {
    expect(BACKFILL_POLL_INTERVAL_MS).toBeGreaterThanOrEqual(10_000);
  });
});
