import { configureStore } from '@reduxjs/toolkit';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { api } from './api';
import { dailyChallengeApis } from './dailyChallengeApis';
import { examApis } from './examApis';
import { repositoryApis } from './repositoryApis';
import { reviewApis } from './reviewApis';
import { statsApis } from './statsApis';

// 不經 HTTP：換掉 baseQuery，數每個 URL 被打了幾次，看別的功能的快取有沒有被重抓。
const mocks = vi.hoisted(() => ({ baseQuery: vi.fn() }));
vi.mock('./baseQuery', () => ({
  apiBaseQuery:
    () =>
    (...args: unknown[]) =>
      mocks.baseQuery(...args),
}));

function makeStore() {
  return configureStore({
    reducer: { [api.reducerPath]: api.reducer },
    middleware: (getDefault) => getDefault().concat(api.middleware),
  });
}

const urlOf = (args: unknown) => (typeof args === 'string' ? args : (args as { url: string }).url);

const calls = (url: string) =>
  mocks.baseQuery.mock.calls.filter(([args]) => urlOf(args) === url).length;

/** 讀取與寫入一律成功，除了 `failing` 裡的網址回 500。 */
function serve(failing: string[] = []) {
  mocks.baseQuery.mockImplementation(async (args: unknown) => {
    const url = urlOf(args);
    if (failing.includes(url)) return { error: { status: 500 } };
    if (url === 'review/count') return { data: { data: { dueCount: 3 } } };
    return { data: { data: {} } };
  });
}

/** 先載入別的功能的資料：待複習數、週報、每日挑戰的連續天數／XP 卡、今天的挑戰。 */
async function loadEverythingOnce(store: ReturnType<typeof makeStore>) {
  await store.dispatch(reviewApis.endpoints.getDueCount.initiate());
  await store.dispatch(statsApis.endpoints.getWeeklyReport.initiate());
  await store.dispatch(dailyChallengeApis.endpoints.getStats.initiate());
  await store.dispatch(dailyChallengeApis.endpoints.getChallenge.initiate());
  for (const url of [
    'review/count',
    'stats/weekly-report',
    'dailychallenge/stats',
    'dailychallenge',
  ])
    expect(calls(url)).toBe(1);
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 20));

describe('shared api: one feature’s change refreshes the others', () => {
  beforeEach(() => {
    mocks.baseQuery.mockReset();
  });

  it('加入單字成功後，重抓待複習數與學習統計', async () => {
    serve();
    const store = makeStore();
    await loadEverythingOnce(store);

    await store.dispatch(repositoryApis.endpoints.putRepositoryWord.initiate('w1')).unwrap();

    await vi.waitFor(() => {
      expect(calls('review/count')).toBe(2);
      expect(calls('stats/weekly-report')).toBe(2);
    });
  });

  it('加入單字失敗時，什麼都不重抓', async () => {
    serve(['repository/word/w1']);
    const store = makeStore();
    await loadEverythingOnce(store);

    await expect(
      store.dispatch(repositoryApis.endpoints.putRepositoryWord.initiate('w1')).unwrap(),
    ).rejects.toBeDefined();
    await settle();

    expect(calls('review/count')).toBe(1);
    expect(calls('stats/weekly-report')).toBe(1);
  });

  it('送出複習後，每日挑戰的連續天數／XP 卡也重抓（答對有 XP），今天的挑戰不動', async () => {
    serve();
    const store = makeStore();
    await loadEverythingOnce(store);

    await store.dispatch(reviewApis.endpoints.submitReviews.initiate({ reviews: [] })).unwrap();

    await vi.waitFor(() => {
      expect(calls('review/count')).toBe(2);
      expect(calls('stats/weekly-report')).toBe(2);
      expect(calls('dailychallenge/stats')).toBe(2);
    });
    expect(calls('dailychallenge')).toBe(1);
  });

  it('記錄作答後，重抓待複習數與學習統計（伺服器重排了那個字）', async () => {
    serve();
    const store = makeStore();
    await loadEverythingOnce(store);

    await store
      .dispatch(examApis.endpoints.recordExamResult.initiate({ examType: 'Cloze', correct: true }))
      .unwrap();

    await vi.waitFor(() => {
      expect(calls('review/count')).toBe(2);
      expect(calls('stats/weekly-report')).toBe(2);
    });
  });

  it('完成每日挑戰後，重抓挑戰本身與學習統計，待複習數不動', async () => {
    serve();
    const store = makeStore();
    await loadEverythingOnce(store);

    await store
      .dispatch(dailyChallengeApis.endpoints.submitChallenge.initiate({ answers: [] }))
      .unwrap();

    await vi.waitFor(() => {
      expect(calls('dailychallenge')).toBe(2);
      expect(calls('dailychallenge/stats')).toBe(2);
      expect(calls('stats/weekly-report')).toBe(2);
    });
    expect(calls('review/count')).toBe(1);
  });
});
