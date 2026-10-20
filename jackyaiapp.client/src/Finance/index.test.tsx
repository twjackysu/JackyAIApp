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

const SIGNED_IN_USER = { id: 'u1', name: 'Jacky', email: 'j@example.com' };

/** Who the page is told is signed in; `unknown` is a user-info request that has not answered yet. */
type Visitor = 'signedIn' | 'signedOut' | 'unknown';

let answerUserInfo: (visitor: 'signedIn' | 'signedOut') => void;

async function mount(visitor: Visitor, entry = '/finance') {
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

  const userInfo = (v: 'signedIn' | 'signedOut') =>
    jsonResponse({ data: v === 'signedIn' ? SIGNED_IN_USER : null });
  let pendingUserInfo: ((r: Response) => void) | undefined;
  answerUserInfo = (v) => pendingUserInfo?.(userInfo(v));

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      if (!request.url.includes('/account/info')) return jsonResponse({ data: null });
      if (visitor === 'unknown')
        return new Promise<Response>((resolve) => (pendingUserInfo = resolve));
      return userInfo(visitor);
    }),
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

const tabs = () => [...container.querySelectorAll<HTMLElement>('[role="tab"]')];
const tabLabels = () => tabs().map((t) => t.textContent?.trim());
const selectedTab = () =>
  tabs()
    .find((t) => t.getAttribute('aria-selected') === 'true')
    ?.textContent?.trim();

const clickTab = (label: string) =>
  act(async () => {
    tabs()
      .find((t) => t.textContent?.trim() === label)!
      .click();
  });

const assistant = () => container.querySelector('[data-testid="ai-assistant"]');
/** The wrapper the page puts around the assistant, which is what gets hidden. */
const assistantWrapper = () => assistant()?.parentElement ?? null;
const SIGN_IN_PROMPT = 'AI 助手需要登入才能使用';

describe('Finance page — AI assistant tab', () => {
  it('has the assistant first, where 多股比較 used to be the last', async () => {
    await mount('signedIn');
    expect(tabLabels()).toEqual(['AI 助手', '綜合量化分析', 'AI 趨勢分析', '今日市場AI摘要']);
  });

  it('opens on the assistant, ready to use', async () => {
    await mount('signedIn');

    expect(selectedTab()).toBe('AI 助手');
    expect(assistant()).not.toBeNull();
    expect(panel.active).toBe(true);
    expect(container.textContent).not.toContain(SIGN_IN_PROMPT);
    expect(getComputedStyle(assistantWrapper()!).display).not.toBe('none');
  });

  it('asks a signed-out visitor to sign in instead of showing the assistant', async () => {
    await mount('signedOut');

    expect(selectedTab()).toBe('AI 助手');
    expect(container.textContent).toContain(SIGN_IN_PROMPT);
    expect(assistant()).toBeNull();
  });

  it('does not tell a signed-in user to sign in while it is still finding out who they are', async () => {
    await mount('unknown');
    expect(container.textContent).not.toContain(SIGN_IN_PROMPT);
    expect(container.querySelector('[role="progressbar"]')).not.toBeNull();
    expect(assistant()).toBeNull();

    await act(async () => answerUserInfo('signedIn'));
    await settle();
    expect(container.textContent).not.toContain(SIGN_IN_PROMPT);
    expect(assistant()).not.toBeNull();
  });

  it('does ask once it knows the visitor is signed out', async () => {
    await mount('unknown');
    await act(async () => answerUserInfo('signedOut'));
    await settle();

    expect(container.textContent).toContain(SIGN_IN_PROMPT);
  });

  it('keeps the same assistant (and so its conversation) when another tab is visited and back', async () => {
    await mount('signedIn');

    await clickTab('綜合量化分析');
    expect(getComputedStyle(assistantWrapper()!).display).toBe('none');
    expect(panel.active).toBe(false);

    await clickTab('AI 助手');
    expect(getComputedStyle(assistantWrapper()!).display).not.toBe('none');
    expect(panel.active).toBe(true);
    expect(panel.mounts).toBe(1);
  });

  it('shows the stock search only on the analysis tabs, not on the assistant', async () => {
    await mount('signedIn');
    const searchBox = () => container.querySelector('input[placeholder^="股票代碼"]');
    expect(searchBox()).toBeNull();

    await clickTab('綜合量化分析');
    expect(searchBox()).not.toBeNull();

    await clickTab('AI 助手');
    expect(searchBox()).toBeNull();
  });

  it('a link to a stock (?symbol=) still lands on its analysis, not on the assistant', async () => {
    await mount('signedIn', '/finance?symbol=2330&market=TW');

    expect(selectedTab()).toBe('綜合量化分析');
    expect(container.querySelector<HTMLInputElement>('input[placeholder^="股票代碼"]')?.value).toBe(
      '2330',
    );
  });
});
