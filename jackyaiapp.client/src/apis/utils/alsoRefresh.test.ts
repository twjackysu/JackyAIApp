import { configureStore } from '@reduxjs/toolkit';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { repositoryApis } from '../repositoryApis';
import { reviewApis } from '../reviewApis';
import { statsApis } from '../statsApis';

// 不經 HTTP：換掉 baseQuery，數每個 URL 被打了幾次，看別的 slice 的快取有沒有被重抓。
const mocks = vi.hoisted(() => ({ baseQuery: vi.fn() }));
vi.mock('../baseQuery', () => ({
  apiBaseQuery:
    () =>
    (...args: unknown[]) =>
      mocks.baseQuery(...args),
}));

function makeStore() {
  return configureStore({
    reducer: {
      [repositoryApis.reducerPath]: repositoryApis.reducer,
      [reviewApis.reducerPath]: reviewApis.reducer,
      [statsApis.reducerPath]: statsApis.reducer,
    },
    middleware: (getDefault) =>
      getDefault().concat(repositoryApis.middleware, reviewApis.middleware, statsApis.middleware),
  });
}

const urlOf = (args: unknown) => (typeof args === 'string' ? args : (args as { url: string }).url);

const calls = (url: string) =>
  mocks.baseQuery.mock.calls.filter(([args]) => urlOf(args) === url).length;

/** 讀取一律成功；`word/w1` 的 PUT 依 `putFails` 成功或失敗。 */
function serve({ putFails }: { putFails: boolean }) {
  mocks.baseQuery.mockImplementation(async (args: unknown) => {
    const url = urlOf(args);
    if (url === 'count') return { data: { data: { dueCount: 3 } } };
    if (url === 'weekly-report') return { data: { data: {} } };
    if (url === 'word/w1') return putFails ? { error: { status: 500 } } : { data: { data: {} } };
    throw new Error(`unexpected request: ${url}`);
  });
}

async function loadReviewQueueAndStats(store: ReturnType<typeof makeStore>) {
  await store.dispatch(reviewApis.endpoints.getDueCount.initiate());
  await store.dispatch(statsApis.endpoints.getWeeklyReport.initiate());
  expect(calls('count')).toBe(1);
  expect(calls('weekly-report')).toBe(1);
}

describe('alsoRefresh', () => {
  beforeEach(() => {
    mocks.baseQuery.mockReset();
  });

  it('加入單字成功後，重抓複習佇列（待複習數）與學習統計', async () => {
    serve({ putFails: false });
    const store = makeStore();
    await loadReviewQueueAndStats(store);

    await store.dispatch(repositoryApis.endpoints.putRepositoryWord.initiate('w1')).unwrap();

    await vi.waitFor(() => {
      expect(calls('count')).toBe(2);
      expect(calls('weekly-report')).toBe(2);
    });
  });

  it('加入單字失敗時，不重抓別的 slice', async () => {
    serve({ putFails: true });
    const store = makeStore();
    await loadReviewQueueAndStats(store);

    await expect(
      store.dispatch(repositoryApis.endpoints.putRepositoryWord.initiate('w1')).unwrap(),
    ).rejects.toBeDefined();
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(calls('count')).toBe(1);
    expect(calls('weekly-report')).toBe(1);
  });
});
