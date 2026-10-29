import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { AppDraftContext } from './appDraft';
import McpAppCardView from './McpAppCardView';

// The app's ui/message as the card handles it (D-204): offered under the app, put in the box only on the reader's press.
vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetMcpAppQuery: () => ({
    data: {
      html: '<html><body>app</body></html>',
      connectDomains: [],
      resourceDomains: [],
      prefersBorder: null,
      connector: '時間',
      host: 'time.example.com',
    },
    isLoading: false,
  }),
  useCallFromMcpAppMutation: () => [vi.fn()],
}));

const card = {
  kind: 'mcp-app',
  title: null,
  data: {
    installationId: 'i1',
    connector: '時間',
    tool: 'get-time',
    title: 'Get Time',
    resourceUri: 'ui://get-time/mcp-app.html',
    input: {},
    result: { content: [{ type: 'text', text: '2026-10-30T01:00:00Z' }] },
  },
};

let container: HTMLDivElement | null = null;
afterEach(() => {
  container?.remove();
  container = null;
});

function mount(putInDraft: ((text: string) => void) | null) {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  act(() =>
    root.render(
      <AppDraftContext.Provider value={putInDraft}>
        <McpAppCardView card={card} />
      </AppDraftContext.Provider>,
    ),
  );
  const frame = container.querySelector('iframe')!;
  const replies: unknown[] = [];
  vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation((m: unknown) => {
    replies.push(m);
  });
  const fromApp = (data: unknown) =>
    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data, source: frame.contentWindow }));
    });
  return { fromApp, replies, html: () => container!.innerHTML };
}

const message = {
  jsonrpc: '2.0',
  id: 7,
  method: 'ui/message',
  params: { role: 'user', content: [{ type: 'text', text: '幫我把時間換成台北時間' }] },
};

const button = (name: string) =>
  [...container!.querySelectorAll('button')].find((b) => b.textContent === name);

describe('an MCP App’s ui/message', () => {
  it('is offered under the app and goes in the box only when the reader presses', () => {
    const putInDraft = vi.fn();
    const { fromApp, replies, html } = mount(putInDraft);

    fromApp(message);

    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 7, result: {} });
    expect(html()).toContain('介面想把這段話放進輸入框');
    expect(html()).toContain('幫我把時間換成台北時間');
    expect(putInDraft).not.toHaveBeenCalled();

    act(() => button('放進輸入框')!.click());

    expect(putInDraft).toHaveBeenCalledWith('幫我把時間換成台北時間');
    expect(html()).not.toContain('介面想把這段話放進輸入框');
  });

  it('keeps the text on screen until the reader decides: a second one does not replace it', () => {
    const putInDraft = vi.fn();
    const { fromApp } = mount(putInDraft);

    fromApp(message);
    fromApp({
      ...message,
      id: 8,
      params: { role: 'user', content: [{ type: 'text', text: '換掉的字' }] },
    });
    act(() => button('放進輸入框')!.click());

    expect(putInDraft).toHaveBeenCalledWith('幫我把時間換成台北時間');
  });

  it('is dropped on 不用', () => {
    const putInDraft = vi.fn();
    const { fromApp, html } = mount(putInDraft);

    fromApp(message);
    act(() => button('不用')!.click());

    expect(putInDraft).not.toHaveBeenCalled();
    expect(html()).not.toContain('介面想把這段話放進輸入框');
  });

  it('is refused where no assistant takes it', () => {
    const { fromApp, replies, html } = mount(null);

    fromApp(message);

    expect(replies).toContainEqual(
      expect.objectContaining({ id: 7, error: expect.objectContaining({ code: -32601 }) }),
    );
    expect(html()).not.toContain('介面想把這段話放進輸入框');
  });

  it('comes only from its own frame', () => {
    const putInDraft = vi.fn();
    const { html } = mount(putInDraft);

    act(() => {
      window.dispatchEvent(new MessageEvent('message', { data: message, source: window }));
    });

    expect(html()).not.toContain('介面想把這段話放進輸入框');
  });
});
