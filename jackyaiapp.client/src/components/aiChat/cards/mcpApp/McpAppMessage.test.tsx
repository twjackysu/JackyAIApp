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

// A press counts only PRESS_GUARD_MS after what it answers appeared (D-232): the clock is moved on past it, except where a
// test presses too soon on purpose.
let clock = 0;
vi.spyOn(performance, 'now').mockImplementation(() => clock);
const press = (name: string, after = 1000) => {
  clock += after;
  act(() => button(name)!.click());
};

describe('an MCP App’s ui/message', () => {
  it('is offered under the app and goes in the box only when the reader presses', () => {
    const putInDraft = vi.fn();
    const { fromApp, replies, html } = mount(putInDraft);

    fromApp(message);

    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 7, result: {} });
    expect(html()).toContain('介面想把這段話放進輸入框');
    expect(html()).toContain('幫我把時間換成台北時間');
    expect(putInDraft).not.toHaveBeenCalled();

    press('放進輸入框');

    expect(putInDraft).toHaveBeenCalledWith('幫我把時間換成台北時間');
    expect(html()).not.toContain('介面想把這段話放進輸入框');
  });

  it('keeps the text on screen until the reader decides: a second one does not replace it', () => {
    const putInDraft = vi.fn();
    const { fromApp, replies } = mount(putInDraft);

    fromApp(message);
    fromApp({
      ...message,
      id: 8,
      params: { role: 'user', content: [{ type: 'text', text: '換掉的字' }] },
    });

    // The app is told the second one was not shown, not that it was (D-211).
    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 7, result: {} });
    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 8, result: { isError: true } });
    press('放進輸入框');

    expect(putInDraft).toHaveBeenCalledWith('幫我把時間換成台北時間');
  });

  it('takes the next one once the reader has decided on the first', () => {
    const putInDraft = vi.fn();
    const { fromApp, replies, html } = mount(putInDraft);

    fromApp(message);
    press('不用');
    fromApp({
      ...message,
      id: 8,
      params: { role: 'user', content: [{ type: 'text', text: '第二則' }] },
    });

    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 8, result: {} });
    expect(html()).toContain('第二則');
  });

  it('is dropped on 不用', () => {
    const putInDraft = vi.fn();
    const { fromApp, html } = mount(putInDraft);

    fromApp(message);
    press('不用');

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

describe('an MCP App’s offer to put words in the box (D-232)', () => {
  it('takes no press right after it appeared', () => {
    const putInDraft = vi.fn();
    const { fromApp } = mount(putInDraft);

    fromApp(message);
    press('放進輸入框', 100);

    expect(putInDraft).not.toHaveBeenCalled();
    press('放進輸入框');
    expect(putInDraft).toHaveBeenCalledWith('幫我把時間換成台北時間');
  });
});

const link = (id: number, url: string) => ({
  jsonrpc: '2.0',
  id,
  method: 'ui/open-link',
  params: { url },
});

describe('an MCP App’s ui/open-link (D-232)', () => {
  it('is offered, and opened only on the reader’s press', () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    const { fromApp, replies, html } = mount(null);

    fromApp(link(3, 'https://example.com/a'));

    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 3, result: {} });
    expect(html()).toContain('介面想開啟 example.com');
    expect(open).not.toHaveBeenCalled();

    press('開啟');

    expect(open).toHaveBeenCalledWith('https://example.com/a', '_blank', 'noopener,noreferrer');
    expect(html()).not.toContain('介面想開啟');
    open.mockRestore();
  });

  it('keeps the address on screen until the reader decides: a second one does not replace it', () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    const { fromApp, replies, html } = mount(null);

    fromApp(link(3, 'https://example.com/a'));
    fromApp(link(4, 'https://evil.example/b'));

    expect(replies).toContainEqual({ jsonrpc: '2.0', id: 4, result: { isError: true } });
    expect(html()).not.toContain('evil.example');
    press('開啟');

    expect(open).toHaveBeenCalledWith('https://example.com/a', '_blank', 'noopener,noreferrer');
    open.mockRestore();
  });

  it('takes no press right after it appeared, nor right after the app resized', () => {
    const open = vi.spyOn(window, 'open').mockImplementation(() => null);
    const { fromApp } = mount(null);

    fromApp(link(3, 'https://example.com/a'));
    press('開啟', 100);
    expect(open).not.toHaveBeenCalled();

    clock += 1000;
    fromApp({ jsonrpc: '2.0', method: 'ui/notifications/size-changed', params: { height: 400 } });
    press('開啟', 100);
    expect(open).not.toHaveBeenCalled();

    press('開啟');
    expect(open).toHaveBeenCalledTimes(1);
    open.mockRestore();
  });
});
