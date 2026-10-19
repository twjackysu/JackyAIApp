import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { BenchmarkChart } from './BenchmarkChart';

import type { BenchmarkComparison, HistoryBackfill } from '@/apis/portfolioApis/types';

const mocks = vi.hoisted(() => ({ benchmark: vi.fn() }));

vi.mock('@/apis/portfolioApis', () => ({
  useGetBenchmarkComparisonQuery: (...args: unknown[]) => mocks.benchmark(...args),
}));
// ECharts needs a canvas, which jsdom doesn't have; only the surrounding UI is under test.
vi.mock('@/charts/ReactECharts', () => ({ default: () => null }));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 12));
  mocks.benchmark.mockReset();
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

const comparison = (
  backfill: HistoryBackfill | null | undefined,
  firstDate = '2026-02-03',
): BenchmarkComparison => ({
  dates: [firstDate, '2026-10-18'],
  portfolioTWD: [1_000_000, 1_100_000],
  portfolioReturnPct: 10,
  benchmarks: [{ symbol: '0050', valuesTWD: [1_000_000, 1_050_000], returnPct: 5 }],
  backfill,
});

const respondWith = (data: BenchmarkComparison) => {
  const res = { success: true, data };
  mocks.benchmark.mockReturnValue({
    data: res,
    currentData: res,
    isLoading: false,
    isFetching: false,
  });
};

/** The query options the chart last asked the hook for (the hook's second argument). */
const lastOptions = () =>
  mocks.benchmark.mock.lastCall?.[1] as
    | { pollingInterval: number; skipPollingIfUnfocused: boolean }
    | undefined;

const render = () => {
  act(() => root.render(<BenchmarkChart />));
  return container.textContent ?? '';
};

describe('BenchmarkChart backfill', () => {
  it('polls and says what is being fetched while the backfill is Running', () => {
    respondWith(comparison(status('Running')));
    const text = render();
    expect(text).toContain('正在補齊');
    expect(text).toContain('2026-02-03');
    expect(text).toContain('2025-10-19');
    expect(lastOptions()?.pollingInterval).toBeGreaterThanOrEqual(10_000);
    expect(lastOptions()?.skipPollingIfUnfocused).toBe(true);
  });

  it('stops polling once the response is no longer Running', () => {
    respondWith(comparison(status('Running')));
    render();
    expect(lastOptions()?.pollingInterval).toBeGreaterThan(0);

    respondWith(comparison(status('Complete')));
    const text = render();
    expect(lastOptions()?.pollingInterval).toBe(0);
    expect(text).not.toContain('正在補齊');
  });

  it('does not poll a Stalled backfill, and says refreshing tries again', () => {
    respondWith(comparison(status('Stalled')));
    const text = render();
    expect(text).toContain('暫時抓不到');
    expect(lastOptions()?.pollingInterval).toBe(0);
  });

  it('says where the chart starts, and does not poll, when there is no backfill', () => {
    // Default range is 3M, so ask for 1Y first.
    respondWith(comparison(null, '2026-03-02'));
    act(() => root.render(<BenchmarkChart />));
    const oneYear = [...container.querySelectorAll('button')].find((b) => b.textContent === '1Y');
    act(() => oneYear!.click());
    const text = container.textContent ?? '';
    expect(text).toContain('圖從 2026-03-02 開始');
    expect(text).not.toContain('6 個月');
    expect(lastOptions()?.pollingInterval).toBe(0);
  });
});
