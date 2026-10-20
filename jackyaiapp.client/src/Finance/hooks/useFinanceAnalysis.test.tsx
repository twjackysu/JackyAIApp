import { configureStore } from '@reduxjs/toolkit';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { TAB_AI_TREND, TAB_QUANTITATIVE, useFinanceAnalysis } from './useFinanceAnalysis';

import { accountApis } from '@/apis/accountApis';
import { financeApis } from '@/apis/financeApis';

// React 19's `act` (imported from 'react' below) warns unless this is set — normally done by
// a testing-library setup file, which this project doesn't have.
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

type HookApi = ReturnType<typeof useFinanceAnalysis>;

function Harness({ capture }: { capture: (api: HookApi) => void }) {
  const api = useFinanceAnalysis();
  capture(api);
  return null;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const GOOD_ANALYSIS = {
  companyName: 'Test Co',
  market: 'TW',
  latestClose: 100,
  indicators: [],
  scoring: null,
  risk: null,
  dataRange: '',
  configuration: {
    includeTechnical: true,
    includeChip: true,
    includeFundamental: true,
    includeScoring: true,
    includeRisk: true,
    onlyIndicators: [],
    excludeIndicators: [],
  },
  generatedAt: new Date().toISOString(),
};

describe('useFinanceAnalysis — comprehensiveError stays on its own tab', () => {
  let container: HTMLDivElement;
  let root: Root;
  let latest: HookApi;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);

    const store = configureStore({
      reducer: {
        [financeApis.reducerPath]: financeApis.reducer,
        [accountApis.reducerPath]: accountApis.reducer,
      },
      middleware: (getDefaultMiddleware) =>
        getDefaultMiddleware().concat(financeApis.middleware, accountApis.middleware),
    });

    // Node's global `Request` (undici) rejects the app's relative API paths ("/api/finance/...")
    // outright — real browsers resolve those against `location`, but nothing here provides that
    // base. Only fixing up the URL; every other Request/fetch behavior stays real.
    const OriginalRequest = globalThis.Request;
    class AbsoluteUrlRequest extends OriginalRequest {
      constructor(input: RequestInfo | URL, init?: RequestInit) {
        super(
          typeof input === 'string' && input.startsWith('/') ? `http://localhost${input}` : input,
          init,
        );
      }
    }
    vi.stubGlobal('Request', AbsoluteUrlRequest);

    // Mock `getComprehensiveAnalysis` so the stock code "BAD" fails while others succeed,
    // exercising the actual interaction (a real dispatched thunk through fetchBaseQuery)
    // instead of hand-constructing hook state.
    vi.stubGlobal(
      'fetch',
      vi.fn(async (request: Request) => {
        const url = request.url;
        if (url.includes('/user-info')) return jsonResponse({ data: null });
        if (url.includes('/comprehensive-analysis')) {
          const body = JSON.parse((await request.text()) || '{}') as { stockCode: string };
          if (body.stockCode === 'BAD') {
            return jsonResponse({ error: { code: 1050000, message: 'boom' } }, 500);
          }
          return jsonResponse({ data: { stockCode: body.stockCode, ...GOOD_ANALYSIS } });
        }
        return jsonResponse({ data: null });
      }),
    );

    act(() => {
      root = createRoot(container);
      root.render(
        <Provider store={store}>
          <MemoryRouter>
            <Harness
              capture={(api) => {
                latest = api;
              }}
            />
          </MemoryRouter>
        </Provider>,
      );
    });
  });

  afterEach(() => {
    act(() => {
      root.unmount();
    });
    container.remove();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('shows showComprehensiveErrorAlert on the quantitative tab after its own failed search', async () => {
    expect(latest.analysisTab).toBe(TAB_QUANTITATIVE);
    act(() => {
      latest.setStockSearchTerm('BAD');
    });

    await act(async () => {
      await latest.handleStockSearch();
    });

    expect(latest.showComprehensiveErrorAlert).toBe(true);
  });

  it('clears a stale comprehensiveError when switching tabs', async () => {
    act(() => {
      latest.setStockSearchTerm('BAD');
    });
    await act(async () => {
      await latest.handleStockSearch();
    });
    expect(latest.showComprehensiveErrorAlert).toBe(true);

    act(() => {
      latest.handleTabChange(null, TAB_AI_TREND);
    });

    expect(latest.comprehensiveError).toBeUndefined();
    expect(latest.showComprehensiveErrorAlert).toBe(false);
  });
});
