import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DrawdownChart } from './DrawdownChart';

import type { DrawdownAnalysis, HistoryBackfill } from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({ drawdown: vi.fn() }));

vi.mock('@/apis/portfolioApis', () => ({
  useGetDrawdownQuery: (...args: unknown[]) => mocks.drawdown(...args),
}));
// ECharts needs a canvas, which jsdom doesn't have; only the surrounding UI is under test.
vi.mock('@/charts/ReactECharts', () => ({ default: () => null }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 12));
  mocks.drawdown.mockReset();
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.useRealTimers();
});

const status = (state: HistoryBackfill['state']): HistoryBackfill => ({
  state,
  coveredFrom: '2026-02-03',
  targetFrom: '2025-10-19',
});

const analysis = (
  backfill: HistoryBackfill | null | undefined,
  firstDate = '2026-02-03',
): DrawdownAnalysis => ({
  points: [firstDate, '2026-10-18'].map((date) => ({
    date,
    totalTWD: 1_000_000,
    runningPeakTWD: 1_000_000,
    runningPeakDate: firstDate,
    drawdownPct: 0,
  })),
  maxDrawdownPct: 0,
  peakDate: null,
  troughDate: null,
  recoveryDate: null,
  underwaterDays: null,
  stillUnderwater: false,
  currentDrawdownPct: 0,
  backfill,
});

const respondWith = (data: DrawdownAnalysis) => {
  const res = { success: true, data };
  mocks.drawdown.mockReturnValue({
    data: res,
    currentData: res,
    isLoading: false,
    isFetching: false,
  });
};

/** The `pollingInterval` the chart last asked the query hook for. */
const lastPolling = () => {
  const options = mocks.drawdown.mock.lastCall?.[1] as
    { pollingInterval: number; skipPollingIfUnfocused: boolean } | undefined;
  return options;
};

const render = () => {
  act(() => root.render(<DrawdownChart />));
  return container.textContent ?? '';
};

describe('DrawdownChart backfill', () => {
  it('polls and says what is being fetched while the backfill is Running', () => {
    respondWith(analysis(status('Running')));
    const text = render();
    expect(text).toContain('正在補齊');
    expect(text).toContain('2026-02-03');
    expect(text).toContain('2025-10-19');
    expect(lastPolling()?.pollingInterval).toBeGreaterThanOrEqual(10_000);
    expect(lastPolling()?.skipPollingIfUnfocused).toBe(true);
  });

  it('stops polling once the response is no longer Running', () => {
    respondWith(analysis(status('Running')));
    render();
    expect(lastPolling()?.pollingInterval).toBeGreaterThan(0);

    respondWith(analysis(status('Complete')));
    const text = render();
    expect(lastPolling()?.pollingInterval).toBe(0);
    expect(text).not.toContain('正在補齊');
  });

  it('does not poll a Stalled backfill, and says refreshing tries again', () => {
    respondWith(analysis(status('Stalled')));
    const text = render();
    expect(text).toContain('暫時抓不到');
    expect(text).toContain('重新整理');
    expect(lastPolling()?.pollingInterval).toBe(0);
  });

  it('does not poll and shows no "6 個月" note when Complete', () => {
    respondWith(analysis(status('Complete'), '2025-10-20'));
    const text = render();
    expect(text).not.toContain('6 個月');
    expect(lastPolling()?.pollingInterval).toBe(0);
  });

  it('does not poll or note anything for a range with no backfill that starts on time', () => {
    respondWith(analysis(null, '2026-07-22'));
    const text = render();
    expect(text).not.toContain('補齊');
    expect(text).not.toContain('圖從');
    expect(lastPolling()?.pollingInterval).toBe(0);
  });
});
