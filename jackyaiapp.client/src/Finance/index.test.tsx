import { configureStore } from '@reduxjs/toolkit';
import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Finance from '.';

import { accountApis } from '@/apis/accountApis';
import { financeApis } from '@/apis/financeApis';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// The page's widgets are exercised by their own tests; here only the page's own wiring matters.
vi.mock('./components', () => ({
  StockAnalysisResult: () => null,
  ComprehensiveAnalysisResult: () => null,
  AnalysisConfigPanel: () => null,
  MarketOverview: () => null,
  IndicatorExplorer: () => null,
  MarketSeasonalChart: () => null,
  TaiexTrendlineCard: () => null,
}));
vi.mock('./components/MarketSummaryTab', () => ({ MarketSummaryTab: () => null }));

// Counts how often the panel is created, to tell "kept mounted" from "mounted again".
const panel = vi.hoisted(() => ({ mounts: 0, active: undefined as boolean | undefined }));
vi.mock('./components/aiAssistant', () => {
  function StubAssistant({ active }: { active: boolean }) {
    panel.active = active;
    useEffect(() => {
      panel.mounts += 1;
    }, []);
    return <div data-testid="ai-assistant">assistant</div>;
  }
  return { default: StubAssistant };
});

let container: HTMLDivElement;
let root: Root;

const jsonResponse = (body: unknown) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

function mount(signedIn: boolean) {
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
  return act(async () => {
    root.render(
      <Provider store={store}>
        <MemoryRouter>
          <Finance />
        </MemoryRouter>
      </Provider>,
    );
  });
}

beforeEach(() => {
  panel.mounts = 0;
  panel.active = undefined;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const tabLabels = () =>
  [...container.querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim());

const clickTab = (label: string) =>
  act(async () => {
    const tab = [...container.querySelectorAll<HTMLElement>('[role="tab"]')].find(
      (t) => t.textContent?.trim() === label,
    );
    tab!.click();
  });

/** The wrapper the page puts around the assistant, which is what gets hidden. */
const assistantWrapper = () =>
  container.querySelector<HTMLElement>('[data-testid="ai-assistant"]')?.parentElement ?? null;

describe('Finance page — AI assistant tab', () => {
  it('has the assistant where 多股比較 used to be', async () => {
    await mount(true);
    expect(tabLabels()).toEqual(['綜合量化分析', 'AI 趨勢分析', '今日市場AI摘要', 'AI 助手']);
  });

  it('does not mount the assistant until its tab is opened', async () => {
    await mount(true);
    expect(container.querySelector('[data-testid="ai-assistant"]')).toBeNull();
    expect(panel.mounts).toBe(0);
  });

  it('asks a signed-out visitor to sign in instead of showing the assistant', async () => {
    await mount(false);
    await clickTab('AI 助手');

    expect(container.textContent).toContain('AI 助手需要登入才能使用');
    expect(container.querySelector('[data-testid="ai-assistant"]')).toBeNull();
  });

  it('shows the assistant to a signed-in user, with no sign-in prompt', async () => {
    await mount(true);
    await clickTab('AI 助手');

    expect(container.querySelector('[data-testid="ai-assistant"]')).not.toBeNull();
    expect(panel.active).toBe(true);
    expect(container.textContent).not.toContain('AI 助手需要登入才能使用');
    expect(getComputedStyle(assistantWrapper()!).display).not.toBe('none');
  });

  it('keeps the same assistant (and so its conversation) when another tab is visited and back', async () => {
    await mount(true);
    await clickTab('AI 助手');

    await clickTab('綜合量化分析');
    expect(getComputedStyle(assistantWrapper()!).display).toBe('none');
    expect(panel.active).toBe(false);

    await clickTab('AI 助手');
    expect(getComputedStyle(assistantWrapper()!).display).not.toBe('none');
    expect(panel.active).toBe(true);
    expect(panel.mounts).toBe(1);
  });

  it('shows the stock search only on the analysis tabs, not on the assistant', async () => {
    await mount(true);
    const searchBox = () => container.querySelector('input[placeholder^="股票代碼"]');
    expect(searchBox()).not.toBeNull();

    await clickTab('AI 助手');
    expect(searchBox()).toBeNull();
  });
});
