import { configureStore } from '@reduxjs/toolkit';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DrawdownBrakeDto, DrawdownBrakeRefreshDto } from './types';

import { portfolioApis } from './index';

// 不經 HTTP：換掉 baseQuery，只驗證 endpoint 的請求形狀與 onQueryStarted 對快取的寫入。
const mocks = vi.hoisted(() => ({ baseQuery: vi.fn() }));
vi.mock('../baseQuery', () => ({
  apiBaseQuery:
    () =>
    (...args: unknown[]) =>
      mocks.baseQuery(...args),
}));

function signal(asOfDate: string, lastCheckedAt: string | null = null): DrawdownBrakeDto {
  return {
    asOfDate,
    state: 'OFFENSE',
    prevState: 'OFFENSE',
    lastSwitchDate: null,
    close: 100,
    high252: 101,
    sma60: 95,
    drawdownPct: -1,
    distanceToTriggerPct: 7,
    isStale: false,
    isInitializing: false,
    estimate: null,
    estimateCorrected: false,
    lastCheckedAt,
    generatedAt: '2026-10-11T07:06:00Z',
  };
}

function makeStore() {
  return configureStore({
    reducer: { [portfolioApis.reducerPath]: portfolioApis.reducer },
    middleware: (getDefault) => getDefault().concat(portfolioApis.middleware),
  });
}

type Store = ReturnType<typeof makeStore>;

const cached = (store: Store) =>
  portfolioApis.endpoints.getDrawdownBrake.select()(store.getState()).data;

const refresh = (store: Store) =>
  store.dispatch(portfolioApis.endpoints.refreshDrawdownBrake.initiate()).unwrap();

/** GET 回 `before`；POST refresh 回 `reply`（或失敗）。 */
function serve(before: DrawdownBrakeDto, reply: DrawdownBrakeRefreshDto | { error: unknown }) {
  mocks.baseQuery.mockImplementation(async (args: { url: string } | string) => {
    const url = typeof args === 'string' ? args : args.url;
    if (url === 'drawdown-brake') return { data: { data: before } };
    if (url === 'drawdown-brake/refresh') {
      return 'error' in reply ? { error: reply.error } : { data: { data: reply } };
    }
    throw new Error(`unexpected request: ${url}`);
  });
}

describe('refreshDrawdownBrake', () => {
  beforeEach(() => {
    mocks.baseQuery.mockReset();
  });

  it('POST drawdown-brake/refresh（不帶 body）', async () => {
    const before = signal('2026-10-08');
    serve(before, { result: 'NoNewData', retryAfterSeconds: null, signal: before });
    await refresh(makeStore());
    expect(mocks.baseQuery.mock.calls[0][0]).toEqual({
      url: 'drawdown-brake/refresh',
      method: 'POST',
    });
  });

  it.each(['Updated', 'NoNewData', 'SourceUnavailable', 'Throttled'] as const)(
    '%s：用回應的 signal 取代 getDrawdownBrake 快取（含新的 lastCheckedAt）',
    async (result) => {
      const before = signal('2026-10-08', null);
      const latest = signal('2026-10-09', '2026-10-09T07:05:00Z');
      serve(before, {
        result,
        retryAfterSeconds: result === 'Throttled' ? 30 : null,
        signal: latest,
      });
      const store = makeStore();
      await store.dispatch(portfolioApis.endpoints.getDrawdownBrake.initiate()).unwrap();
      expect(cached(store)?.data.asOfDate).toBe('2026-10-08');

      const response = await refresh(store);
      expect(response.data.result).toBe(result);
      await vi.waitFor(() => expect(cached(store)).toEqual({ data: latest }));
    },
  );

  it('第一次載入失敗、快取裡沒有資料時，refresh 的 signal 也能補上', async () => {
    const latest = signal('2026-10-09', '2026-10-09T07:05:00Z');
    serve(signal('2026-10-08'), { result: 'Updated', retryAfterSeconds: null, signal: latest });
    const store = makeStore();
    expect(cached(store)).toBeUndefined();
    await refresh(store);
    await vi.waitFor(() => expect(cached(store)).toEqual({ data: latest }));
  });

  it('請求失敗（403／網路／500）：rejected，快取原封不動', async () => {
    const before = signal('2026-10-08');
    const store = makeStore();
    serve(before, { error: { status: 403, data: { error: { code: 1040301, message: 'no' } } } });
    await store.dispatch(portfolioApis.endpoints.getDrawdownBrake.initiate()).unwrap();

    await expect(refresh(store)).rejects.toMatchObject({ status: 403 });
    // 讓 onQueryStarted 的 catch 有機會跑完，再確認沒有被改動。
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(cached(store)).toEqual({ data: before });
  });
});
