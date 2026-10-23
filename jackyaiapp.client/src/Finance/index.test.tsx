import { configureStore } from '@reduxjs/toolkit';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Finance from '.';

import { accountApis } from '@/apis/accountApis';
import { financeApis } from '@/apis/financeApis';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// The page's widgets and the assistant's panel are exercised by their own tests; here only the
// page's own wiring matters.
vi.mock('./components', () => ({
  StockAnalysisResult: () => null,
  ComprehensiveAnalysisResult: () => null,
  AnalysisConfigPanel: () => null,
  MarketOverview: () => <div data-testid="market-overview" />,
  IndicatorExplorer: () => null,
  MarketSeasonalChart: () => null,
  TaiexTrendlineCard: () => null,
}));
vi.mock('./components/MarketSummaryTab', () => ({ MarketSummaryTab: () => null }));
vi.mock('@/components/aiChat', () => ({
  default: () => <div data-testid="ai-assistant-panel" />,
}));

let container: HTMLDivElement;
let root: Root;

const jsonResponse = (body: unknown) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

async function mount(signedIn: boolean, entry = '/finance') {
  // Node's Request rejects the app's relative API paths; resolve them against a dummy origin.
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
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) =>
      request.url.includes('/account/info')
        ? jsonResponse({
            data: signedIn ? { id: 'u1', name: 'Jacky', email: 'j@example.com' } : null,
          })
        : jsonResponse({ data: null }),
    ),
  );

  const store = configureStore({
    reducer: {
      [financeApis.reducerPath]: financeApis.reducer,
      [accountApis.reducerPath]: accountApis.reducer,
    },
    middleware: (getDefaultMiddleware) =>
      getDefaultMiddleware().concat(financeApis.middleware, accountApis.middleware),
  });
  await act(async () => {
    root.render(
      <Provider store={store}>
        <MemoryRouter initialEntries={[entry]}>
          <Finance />
        </MemoryRouter>
      </Provider>,
    );
  });
  await settle();
}

/** Lets the requests that were just made answer, and the page react to the answers. */
async function settle() {
  for (let i = 0; i < 4; i++) {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });
  }
}

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const tabs = () => [...container.querySelectorAll<HTMLElement>('[role="tab"]')];
const selectedTab = () =>
  tabs()
    .find((t) => t.getAttribute('aria-selected') === 'true')
    ?.textContent?.trim();
const assistantBox = () => container.querySelector<HTMLInputElement>('input[aria-label="AI 助手"]');

/** Whether `a` comes before `b` in the page. */
const precedes = (a: Element, b: Element) =>
  !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);

describe('Finance page', () => {
  it('keeps the three analysis tabs, opening on the quantitative analysis', async () => {
    await mount(true);

    expect(tabs().map((t) => t.textContent?.trim())).toEqual([
      '綜合量化分析',
      'AI 趨勢分析',
      '今日市場AI摘要',
    ]);
    expect(selectedTab()).toBe('綜合量化分析');
  });

  it('puts the AI assistant at the top, as a one-line box above everything else', async () => {
    await mount(true);

    const box = assistantBox()!;
    expect(box).not.toBeNull();
    expect(precedes(box, container.querySelector('[data-testid="market-overview"]')!)).toBe(true);
    expect(precedes(box, tabs()[0])).toBe(true);
    // Folded: the conversation itself is not on the page until the box is used.
    expect(container.querySelector('[data-testid="ai-assistant-panel"]')).toBeNull();
  });

  it('opens the conversation in place once the box is used', async () => {
    await mount(true);
    await act(async () => assistantBox()!.focus());
    await settle();

    expect(container.querySelector('[data-testid="ai-assistant-panel"]')).not.toBeNull();
    expect(assistantBox()).toBeNull();
  });

  it('asks a signed-out visitor to sign in from the box', async () => {
    await mount(false);
    expect(assistantBox()!.placeholder).toContain('登入後');
  });

  it('a link to a stock (?symbol=) still lands on its analysis', async () => {
    await mount(true, '/finance?symbol=2330&market=TW');

    expect(selectedTab()).toBe('綜合量化分析');
    expect(container.querySelector<HTMLInputElement>('input[placeholder^="股票代碼"]')?.value).toBe(
      '2330',
    );
  });
});
