import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { MAX_CALLS_IN_FLIGHT, PAUSED_MESSAGE, TOO_MANY_CALLS_MESSAGE } from './appToolGate';
import McpAppCardView from './McpAppCardView';

// An app's call of a tool for it alone that may change something (D-207): the server says it waits on the reader, the card
// asks, and only 允許 sends it again with readerAllowed.
const mocks = vi.hoisted(() => ({ call: vi.fn() }));
vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetMcpAppQuery: () => ({
    data: {
      html: '<html><body>app</body></html>',
      connectDomains: [],
      resourceDomains: [],
      prefersBorder: null,
      connector: 'System Monitor',
      host: 'monitor.example.com',
    },
    isLoading: false,
  }),
  useCallFromMcpAppMutation: () => [mocks.call],
}));

const card = {
  kind: 'mcp-app',
  title: null,
  data: {
    installationId: 'i1',
    connector: 'System Monitor',
    tool: 'get-system-info',
    title: 'Get System Info',
    resourceUri: 'ui://system-monitor/mcp-app.html',
    input: {},
    result: { content: [{ type: 'text', text: '{}' }] },
  },
};

const needsReader = { status: 403, data: { error: { code: 'AppCallNeedsReader', message: '…' } } };
const answer = (args: { readerAllowed?: boolean }) => ({
  unwrap: () =>
    args.readerAllowed
      ? Promise.resolve({ result: { content: [{ type: 'text', text: 'ok' }] } })
      : Promise.reject(needsReader),
});

let container: HTMLDivElement | null = null;
afterEach(() => {
  container?.remove();
  container = null;
  mocks.call.mockReset();
  vi.unstubAllGlobals();
});

/** jsdom has no IntersectionObserver: one whose `show(false)` says the card scrolled out of sight. */
function stubIntersectionObserver() {
  let report: ((seen: boolean) => void) | null = null;
  class FakeIntersectionObserver {
    constructor(callback: IntersectionObserverCallback) {
      report = (seen) =>
        callback(
          [{ isIntersecting: seen } as IntersectionObserverEntry],
          this as unknown as IntersectionObserver,
        );
    }
    observe() {}
    disconnect() {}
  }
  vi.stubGlobal('IntersectionObserver', FakeIntersectionObserver);
  return { show: (seen: boolean) => act(() => report?.(seen)) };
}

function mount() {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  act(() => root.render(<McpAppCardView card={card} />));
  const frame = container.querySelector('iframe')!;
  const replies: unknown[] = [];
  vi.spyOn(frame.contentWindow!, 'postMessage').mockImplementation((m: unknown) => {
    replies.push(m);
  });
  const callOf = (name: string) => async (id: number) => {
    await act(async () => {
      window.dispatchEvent(
        new MessageEvent('message', {
          data: { jsonrpc: '2.0', id, method: 'tools/call', params: { name } },
          source: frame.contentWindow,
        }),
      );
      await Promise.resolve();
    });
  };
  return {
    poll: callOf('poll-system-stats'),
    other: callOf('reset-stats'),
    replies,
    html: () => container!.innerHTML,
  };
}

const button = (name: string) =>
  [...container!.querySelectorAll('button')].find((b) => b.textContent === name)!;
// A press counts only PRESS_GUARD_MS after the question appeared: the clock is moved on past it.
let clock = 0;
vi.spyOn(performance, 'now').mockImplementation(() => clock);
const press = async (name: string) => {
  clock += 1000;
  await act(async () => {
    button(name).click();
    await Promise.resolve();
  });
};

describe('an MCP App’s tool for it alone that may change something', () => {
  it('is asked about, and runs with readerAllowed once allowed — later calls without asking', async () => {
    mocks.call.mockImplementation(answer);
    const { poll, replies, html } = mount();

    await poll(1);
    expect(html()).toContain('介面想執行「<bdi>poll-system-stats</bdi>」');
    expect(replies).toEqual([]);

    await poll(2);
    expect(mocks.call).toHaveBeenCalledTimes(1);

    await press('允許');
    expect(mocks.call).toHaveBeenCalledWith(expect.objectContaining({ readerAllowed: true }));
    expect(replies).toContainEqual(expect.objectContaining({ id: 1, result: expect.anything() }));
    expect(replies).toContainEqual(expect.objectContaining({ id: 2, result: expect.anything() }));
    expect(html()).not.toContain('介面想執行');
    expect(html()).toContain('已允許介面執行');

    await poll(3);
    expect(mocks.call).toHaveBeenLastCalledWith(expect.objectContaining({ readerAllowed: true }));
    expect(replies).toContainEqual(expect.objectContaining({ id: 3, result: expect.anything() }));
  });

  it('is refused on 不用, and from then on without asking the server', async () => {
    mocks.call.mockImplementation(answer);
    const { poll, replies, html } = mount();

    await poll(1);
    await press('不用');
    expect(replies).toContainEqual(expect.objectContaining({ id: 1, error: expect.anything() }));
    expect(html()).not.toContain('介面想執行');

    await poll(2);
    expect(mocks.call).toHaveBeenCalledTimes(1);
    expect(replies).toContainEqual(expect.objectContaining({ id: 2, error: expect.anything() }));
  });

  it('does not take a press that comes right after the question appeared', async () => {
    mocks.call.mockImplementation(answer);
    const { poll } = mount();

    await poll(1);
    await act(async () => {
      button('允許').click();
      await Promise.resolve();
    });

    expect(mocks.call).toHaveBeenCalledTimes(1);
    expect(mocks.call).not.toHaveBeenCalledWith(expect.objectContaining({ readerAllowed: true }));
  });

  it('asks about one tool at a time: another tool waits with an error, never behind the question', async () => {
    mocks.call.mockImplementation(answer);
    const { poll, replies, other } = mount();

    await poll(1);
    await other(2);
    await press('允許');

    expect(replies).toContainEqual(expect.objectContaining({ id: 2, error: expect.anything() }));
    expect(mocks.call).not.toHaveBeenCalledWith(
      expect.objectContaining({ tool: 'reset-stats', readerAllowed: true }),
    );
  });

  it('stops running once the reader takes the allowance back', async () => {
    mocks.call.mockImplementation(answer);
    const { poll, replies } = mount();

    await poll(1);
    await press('允許');
    await press('停止允許');
    const calls = mocks.call.mock.calls.length;
    await poll(2);

    expect(mocks.call.mock.calls.length).toBe(calls);
    expect(replies).toContainEqual(expect.objectContaining({ id: 2, error: expect.anything() }));
  });

  it('can be allowed after all once refused, from then on without asking', async () => {
    mocks.call.mockImplementation(answer);
    const { poll, replies, html } = mount();

    await poll(1);
    await press('不用');
    expect(html()).toContain('沒有允許介面執行');
    await press('改為允許');
    expect(html()).not.toContain('沒有允許介面執行');
    expect(html()).toContain('已允許介面執行');

    await poll(2);
    expect(mocks.call).toHaveBeenLastCalledWith(expect.objectContaining({ readerAllowed: true }));
    expect(replies).toContainEqual(expect.objectContaining({ id: 2, result: expect.anything() }));
  });

  it('does not take 改為允許 pressed right after 不用 (a double press)', async () => {
    mocks.call.mockImplementation(answer);
    const { poll } = mount();

    await poll(1);
    await press('不用');
    await act(async () => {
      button('改為允許').click();
      await Promise.resolve();
    });

    expect(container!.innerHTML).toContain('沒有允許介面執行');
  });

  it('pauses an allowed tool while the card is out of sight, and goes on once it is seen', async () => {
    const io = stubIntersectionObserver();
    mocks.call.mockImplementation(answer);
    const { poll, replies } = mount();

    await poll(1);
    await press('允許');
    const calls = mocks.call.mock.calls.length;

    io.show(false);
    await poll(2);
    expect(mocks.call.mock.calls.length).toBe(calls);
    expect(replies).toContainEqual(
      expect.objectContaining({
        id: 2,
        error: expect.objectContaining({ message: PAUSED_MESSAGE }),
      }),
    );

    io.show(true);
    await poll(3);
    expect(mocks.call.mock.calls.length).toBe(calls + 1);
    expect(replies).toContainEqual(expect.objectContaining({ id: 3, result: expect.anything() }));
  });
});

describe('an MCP App’s calls at once (D-239)', () => {
  it('sends at most MAX_CALLS_IN_FLIGHT to the server, refusing the rest, and sends again once one is answered', async () => {
    const answers: (() => void)[] = [];
    mocks.call.mockImplementation(() => ({
      unwrap: () =>
        new Promise((resolve) =>
          answers.push(() => resolve({ result: { content: [{ type: 'text', text: 'ok' }] } })),
        ),
    }));
    const { other, replies } = mount();

    for (let id = 1; id <= MAX_CALLS_IN_FLIGHT + 1; id++) await other(id);

    expect(mocks.call).toHaveBeenCalledTimes(MAX_CALLS_IN_FLIGHT);
    expect(replies).toContainEqual(
      expect.objectContaining({
        id: MAX_CALLS_IN_FLIGHT + 1,
        error: expect.objectContaining({ message: TOO_MANY_CALLS_MESSAGE }),
      }),
    );

    await act(async () => {
      answers[0]();
      await Promise.resolve();
    });
    expect(replies).toContainEqual(expect.objectContaining({ id: 1, result: expect.anything() }));
    await other(99);
    expect(mocks.call).toHaveBeenCalledTimes(MAX_CALLS_IN_FLIGHT + 1);
  });
});
