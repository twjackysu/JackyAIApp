import { configureStore } from '@reduxjs/toolkit';
import { act, StrictMode } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { TAB_COMPARISON, TAB_QUANTITATIVE, useFinanceAnalysis } from './useFinanceAnalysis';

import { accountApis } from '@/apis/accountApis';
import { financeApis } from '@/apis/financeApis';

// Required for `act()` to work outside of a helper like @testing-library/react, which this
// project doesn't depend on (see TASK-014's precedent using `renderToStaticMarkup` instead).
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/**
 * Regression test for the bug QA found on `TASK-014`'s PR: `useGetComprehensiveAnalysisMutation`
 * is a single trigger shared by the single-stock quantitative tab and the comparison tab, so RTK
 * Query's `error`/`isLoading` reflect whichever call landed last — including a failing comparison
 * fetch, even though `ComparisonResult` already shows that failure inline per-slot. `Finance/index.tsx`
 * fixed this by gating its quantitative-tab Alert on `analysisTab === TAB_QUANTITATIVE`; this test
 * exercises the real interaction (actual RTK Query dispatch + fetch, not `renderToStaticMarkup`,
 * which can't observe this class of cross-tab state leak) and asserts the gated condition the Alert
 * now uses.
 */

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function buildStore() {
  return configureStore({
    reducer: {
      [accountApis.reducerPath]: accountApis.reducer,
      [financeApis.reducerPath]: financeApis.reducer,
    },
    middleware: (getDefaultMiddleware) =>
      getDefaultMiddleware().concat(accountApis.middleware, financeApis.middleware),
  });
}

let container: HTMLDivElement;
let root: Root;

async function renderHook() {
  const store = buildStore();
  const apiRef: { current: ReturnType<typeof useFinanceAnalysis> | null } = { current: null };

  function TestHarness() {
    apiRef.current = useFinanceAnalysis();
    return null;
  }

  await act(async () => {
    root = createRoot(container);
    root.render(
      <StrictMode>
        <Provider store={store}>
          <MemoryRouter initialEntries={['/finance']}>
            <TestHarness />
          </MemoryRouter>
        </Provider>
      </StrictMode>,
    );
  });

  return apiRef;
}

// `fetchBaseQuery` always builds `new Request(url, config)` itself (see @reduxjs/toolkit's
// rtk-query.modern.mjs) before ever calling `fetch`. The API slices use relative base URLs
// (e.g. `/api/finance`), which the global `Request` this test env resolves against Node's
// undici — it has no notion of a page origin and throws on a relative input. Wrapping the
// global `Request` to resolve against a fixed origin first keeps the slices' relative
// `apiBaseQuery` URLs untouched (a production behavior change would be out of scope here).
const OriginalRequest = globalThis.Request;
class TestOriginRequest extends OriginalRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    const resolvedInput =
      typeof input === 'string' ? new URL(input, 'http://localhost:3000').toString() : input;
    super(resolvedInput, init);
  }
}

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);

  vi.stubGlobal('Request', TestOriginRequest);
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      // `fetchBaseQuery` always calls `fetchFn(request)` with a single `Request` object
      // (never a separate url+init pair), so the body must be read off the request itself.
      const isRequestObject = input instanceof Request;
      const url = isRequestObject ? (input as Request).url : String(input);
      const rawBody = isRequestObject
        ? await (input as Request).text()
        : ((init?.body as string) ?? '');

      // Anonymous visitor — matches the comparison tab's real usage (free, unauthenticated).
      if (url.includes('/api/account/info')) {
        return jsonResponse(401, { error: { code: 1040100, message: 'Not signed in' } });
      }

      if (url.includes('/api/finance/comprehensive-analysis')) {
        const body = JSON.parse(rawBody || '{}');
        if (body.stockCode === 'BADCODE') {
          return jsonResponse(400, { error: { code: 1040000, message: '找不到 BADCODE' } });
        }
        return jsonResponse(200, { data: { stockCode: body.stockCode, categories: [] } });
      }

      throw new Error(`Unexpected fetch call in test: ${url}`);
    }),
  );
});

afterEach(() => {
  act(() => {
    root.unmount();
  });
  container.remove();
  vi.unstubAllGlobals();
});

describe('useFinanceAnalysis — comparison tab error isolation', () => {
  it('does not satisfy the quantitative-tab Alert condition when a comparison fetch fails', async () => {
    const apiRef = await renderHook();

    await act(async () => {
      apiRef.current!.handleTabChange(null, TAB_COMPARISON);
    });
    expect(apiRef.current!.analysisTab).toBe(TAB_COMPARISON);

    // RTK Query's mutation hook reflects whichever `trigger()` call happened *last* — order
    // here matters for the repro: the failing code must be added last so it's the one the
    // shared `comprehensiveError`/`isComprehensiveLoading` end up reflecting, matching how
    // QA actually hit this (see `TASK-016`).
    await act(async () => {
      apiRef.current!.setComparisonInput('GOODCO');
    });
    await act(async () => {
      apiRef.current!.handleAddComparisonCode();
    });
    await act(async () => {
      apiRef.current!.setComparisonInput('BADCODE');
    });
    await act(async () => {
      apiRef.current!.handleAddComparisonCode();
    });
    expect(apiRef.current!.comparisonCodes).toEqual(['GOODCO', 'BADCODE']);

    await act(async () => {
      await apiRef.current!.runComparison();
    });

    // The underlying RTK Query mutation state does leak across calls — this is the mechanism
    // the bug came from, not something this task's fix changes.
    expect(apiRef.current!.comprehensiveError).toBeTruthy();
    expect(apiRef.current!.isComprehensiveLoading).toBe(false);

    // `showComprehensiveError` is what `Finance/index.tsx` actually renders its quantitative-tab
    // Alert on. Must be false while on the comparison tab, even though the raw `comprehensiveError`
    // above is truthy — this is the exact regression QA hit on `TASK-014`'s PR.
    expect(apiRef.current!.showComprehensiveError).toBe(false);

    // The comparison tab's own per-slot error reporting still works correctly.
    const badSlot = apiRef.current!.comparisonSlots.find((slot) => slot.code === 'BADCODE');
    const goodSlot = apiRef.current!.comparisonSlots.find((slot) => slot.code === 'GOODCO');
    expect(badSlot?.status).toBe('error');
    expect(goodSlot?.status).toBe('success');
  });

  it('still shows the quantitative-tab Alert for a failed single-stock search on that tab', async () => {
    const apiRef = await renderHook();

    expect(apiRef.current!.analysisTab).toBe(TAB_QUANTITATIVE);

    await act(async () => {
      apiRef.current!.setStockSearchTerm('BADCODE');
    });
    await act(async () => {
      await apiRef.current!.handleStockSearch();
    });

    expect(apiRef.current!.comprehensiveError).toBeTruthy();
    expect(apiRef.current!.showComprehensiveError).toBe(true);
  });
});
