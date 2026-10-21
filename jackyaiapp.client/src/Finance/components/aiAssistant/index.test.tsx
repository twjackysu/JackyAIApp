import { configureStore } from '@reduxjs/toolkit';
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AiAssistantPanel, { type AssistantActivity } from '.';

import type { ChatConversation, ChatHistoryMessage } from '@/apis/chatbotApis/types';

import { chatbotApis } from '@/apis/chatbotApis';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

const json = (body: unknown) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });

/** The assistant's SSE reply, as ChatbotController writes it. */
const sseReply = (...events: object[]) =>
  new Response(
    new ReadableStream({
      start(controller) {
        const encoder = new TextEncoder();
        for (const event of events)
          controller.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`));
        controller.close();
      },
    }),
    { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
  );

const conversation = (id: string, title: string): ChatConversation => ({
  id,
  title,
  createdAt: '2026-10-19T09:00:00Z',
  updatedAt: '2026-10-19T09:00:00Z',
});

const turn = (id: number, role: 'user' | 'assistant', content: string): ChatHistoryMessage => ({
  id,
  role,
  content,
  createdAt: '2026-10-19T09:00:00Z',
});

// What the server holds, and what was asked of it.
let conversations: ChatConversation[];
let messagesOf: Record<string, ChatHistoryMessage[]>;
let chatReplies: Array<() => Response | Promise<Response>>;
let chatBodies: Array<{ message: string; conversationId: string }>;
let listFetches: number;
let deleted: string[];
/** Called while a chat turn is being answered, to let a test change the server's state first. */
let whileAnswering: (() => void) | undefined;

beforeEach(() => {
  conversations = [];
  messagesOf = {};
  chatReplies = [];
  chatBodies = [];
  listFetches = 0;
  deleted = [];
  whileAnswering = undefined;

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
    vi.fn(async (input: string | Request, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.url;
      const method = typeof input === 'string' ? (init?.method ?? 'GET') : input.method;

      if (url.endsWith('/api/chatbot/chat')) {
        chatBodies.push(JSON.parse(init!.body as string));
        whileAnswering?.();
        return chatReplies.shift()!();
      }
      if (method === 'DELETE') {
        const id = url.split('/').pop()!;
        deleted.push(id);
        conversations = conversations.filter((c) => c.id !== id);
        return json({ data: null });
      }
      const messages = url.match(/conversations\/([^/]+)\/messages$/);
      if (messages) return json({ data: messagesOf[messages[1]] ?? [] });
      if (url.endsWith('/api/chatbot/conversations')) {
        listFetches += 1;
        return json({ data: conversations });
      }
      throw new Error(`Unexpected request: ${method} ${url}`);
    }),
  );

  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

let collapses: number;
let activities: AssistantActivity[];
const lastActivity = () => activities[activities.length - 1];

/** The panel's owner holds the message being typed, as the one-line box does on the page. */
function Harness() {
  const [draft, setDraft] = useState('');
  return (
    <AiAssistantPanel
      active
      inputValue={draft}
      onInputChange={setDraft}
      onCollapse={() => (collapses += 1)}
      onActivityChange={(a) => activities.push(a)}
    />
  );
}

async function mount() {
  collapses = 0;
  activities = [];
  const store = configureStore({
    reducer: { [chatbotApis.reducerPath]: chatbotApis.reducer },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(chatbotApis.middleware),
  });
  await act(async () =>
    root.render(
      <Provider store={store}>
        <Harness />
      </Provider>,
    ),
  );
}

const textarea = () =>
  container.querySelector('textarea:not([aria-hidden="true"])') as HTMLTextAreaElement;

async function send(text: string) {
  act(() => {
    const setValue = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
    setValue.call(textarea(), text);
    textarea().dispatchEvent(new Event('input', { bubbles: true }));
  });
  await act(async () => {
    textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
  });
}

/** Polls `check` until it stops throwing. Each wait is its own `act`, so updates that timers (a
 *  closing drawer's transition) or the network trigger in the meantime are applied between polls. */
async function waitFor(check: () => void, timeoutMs = 2000) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    try {
      check();
      return;
    } catch (error) {
      if (Date.now() > deadline) throw error;
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 25));
      });
    }
  }
}
const waitForText = (text: string) => waitFor(() => expect(container.textContent).toContain(text));

const buttonByLabel = (label: string, within: ParentNode = container) =>
  within.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);

/** A conversation row in the history column (or, given `within`, in the drawer). */
const row = (title: string, within: ParentNode = container) =>
  [...within.querySelectorAll<HTMLElement>('.MuiListItemButton-root')].find((r) =>
    r.textContent?.includes(title),
  )!;

describe('AiAssistantPanel', () => {
  it('is a block with a header and a greeting, not a dialog that can be closed', async () => {
    await mount();

    expect(container.textContent).toContain('AI 助手');
    expect(container.textContent).toContain('您好！我是您的 AI 助手');
    expect(buttonByLabel('新對話')).not.toBeNull();
    expect(buttonByLabel('關閉')).toBeNull();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.querySelector('.MuiFab-root')).toBeNull();
  });

  it('puts the cursor in the message box as soon as it is open', async () => {
    await mount();
    expect(document.activeElement).toBe(textarea());
  });

  it('folds back into its one-line box from the header', async () => {
    await mount();
    act(() => buttonByLabel('收合')!.click());
    expect(collapses).toBe(1);
  });

  it('tells its owner when a reply is being written and when there is a conversation', async () => {
    await mount();
    expect(lastActivity()).toEqual({ busy: false, hasConversation: false });

    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () =>
            resolve(
              sseReply({ type: 'delta', text: '答' }, { type: 'done', conversationId: 'conv-1' }),
            );
        }),
    );
    await send('問');
    expect(lastActivity()).toEqual({ busy: true, hasConversation: true });

    await act(async () => finish());
    await waitForText('答');
    await waitFor(() => expect(lastActivity()).toEqual({ busy: false, hasConversation: true }));
    // Ready for the next message without clicking back into the box.
    expect(document.activeElement).toBe(textarea());
  });

  it('sends the message, shows the streamed reply, and continues the same conversation', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'delta', text: '台積電' },
        { type: 'delta', text: '今天收 1000 元' },
        { type: 'done', conversationId: 'conv-1' },
      ),
    );
    await send('2330 今天如何？');

    await waitForText('台積電今天收 1000 元');
    expect(container.textContent).toContain('2330 今天如何？');
    expect(chatBodies).toEqual([{ message: '2330 今天如何？', conversationId: '' }]);

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '好的' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await send('那 2317 呢？');
    await waitForText('好的');
    expect(chatBodies[1]).toEqual({ message: '那 2317 呢？', conversationId: 'conv-1' });
  });

  it('shows a failed reply inside the conversation instead of leaving an empty bubble', async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'error', message: '額度已用完' }));
    await send('hi');

    await waitForText('錯誤: 額度已用完');
  });

  it('ignores Enter while a reply is still being written', async () => {
    await mount();
    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(sseReply({ type: 'done', conversationId: 'conv-1' }));
        }),
    );
    await send('first');
    expect(textarea().disabled).toBe(true);

    await send('second');
    expect(chatBodies).toHaveLength(1);

    await act(async () => finish());
  });
});

describe('AiAssistantPanel — conversation history column', () => {
  beforeEach(() => {
    conversations = [conversation('c1', '台積電財報'), conversation('c2', 'ETF 比較')];
    messagesOf = {
      c1: [turn(1, 'user', '台積電最新財報？'), turn(2, 'assistant', '毛利率 59%')],
      c2: [turn(3, 'user', '0050 和 006208？'), turn(4, 'assistant', '費用率差不多')],
    };
  });

  it('lists the conversations beside the chat without anything having to be opened', async () => {
    await mount();
    await waitForText('ETF 比較');

    expect(container.textContent).toContain('台積電財報');
    expect(container.querySelector('.MuiDrawer-root')).toBeNull();
  });

  it('opens a conversation from the column and carries on in it', async () => {
    await mount();
    await waitForText('台積電財報');

    await act(async () => row('台積電財報').click());
    await waitForText('毛利率 59%');
    expect(container.textContent).toContain('台積電最新財報？');

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '營收呢？好的' }, { type: 'done', conversationId: 'c1' }),
    );
    await send('營收呢？');
    await waitForText('營收呢？好的');
    expect(chatBodies).toEqual([{ message: '營收呢？', conversationId: 'c1' }]);
  });

  it('shows a conversation as soon as its first reply has come in, and keeps the list current', async () => {
    conversations = [];
    await mount();
    await waitFor(() => expect(listFetches).toBe(1));
    expect(container.textContent).toContain('還沒有任何對話');

    whileAnswering = () => {
      conversations = [conversation('conv-new', '剛問的問題')];
    };
    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '答案' }, { type: 'done', conversationId: 'conv-new' }),
    );
    await send('剛問的問題');

    await waitForText('剛問的問題');
    expect(listFetches).toBeGreaterThan(1);
    expect(row('剛問的問題').classList.contains('Mui-selected')).toBe(true);
  });

  it('starts over with 新對話: the messages go and the next message carries no conversation id', async () => {
    await mount();
    await waitForText('台積電財報');
    await act(async () => row('台積電財報').click());
    await waitForText('毛利率 59%');

    act(() => buttonByLabel('新對話')!.click());
    expect(container.textContent).not.toContain('毛利率 59%');
    expect(container.textContent).toContain('您好！我是您的 AI 助手');

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '全新的' }, { type: 'done', conversationId: 'conv-2' }),
    );
    await send('第二則');
    await waitForText('全新的');
    expect(chatBodies).toEqual([{ message: '第二則', conversationId: '' }]);
  });

  it('deleting the conversation that is open clears the chat as well', async () => {
    await mount();
    await waitForText('台積電財報');
    await act(async () => row('台積電財報').click());
    await waitForText('毛利率 59%');

    await act(async () => buttonByLabel('刪除對話', row('台積電財報'))!.click());

    await waitFor(() => expect(deleted).toEqual(['c1']));
    await waitFor(() => expect(container.textContent).not.toContain('毛利率 59%'));
    expect(container.textContent).toContain('您好！我是您的 AI 助手');
    await waitFor(() => expect(container.textContent).not.toContain('台積電財報'));
  });

  it('cannot be switched or restarted while a reply is being written', async () => {
    await mount();
    await waitForText('台積電財報');
    await act(async () => row('台積電財報').click());
    await waitForText('毛利率 59%');

    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(sseReply({ type: 'done', conversationId: 'c1' }));
        }),
    );
    await send('營收呢？');

    expect(row('ETF 比較').classList.contains('Mui-disabled')).toBe(true);
    expect(buttonByLabel('新對話')!.disabled).toBe(true);
    const newChatInColumn = [...container.querySelectorAll('button')].find(
      (b) => b.textContent === '新對話',
    )!;
    expect(newChatInColumn.disabled).toBe(true);

    await act(async () => finish());
    await waitFor(() => expect(row('ETF 比較').classList.contains('Mui-disabled')).toBe(false));
    expect(buttonByLabel('新對話')!.disabled).toBe(false);
  });

  it('where the column does not fit, the header button slides the same list over the chat', async () => {
    await mount();
    await waitForText('台積電財報');

    await act(async () => buttonByLabel('對話紀錄')!.click());
    const drawer = document.querySelector<HTMLElement>('.MuiDrawer-paper')!;
    expect(drawer).not.toBeNull();
    // Inside the panel, not over the page.
    expect(container.contains(drawer)).toBe(true);

    await act(async () => row('ETF 比較', drawer).click());
    await waitForText('費用率差不多');
    await waitFor(() => expect(document.querySelector('.MuiDrawer-paper')).toBeNull());
  });
});
