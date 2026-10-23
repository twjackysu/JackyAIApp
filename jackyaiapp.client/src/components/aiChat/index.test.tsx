import { configureStore } from '@reduxjs/toolkit';
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AiAssistantPanel, { type AssistantActivity } from '.';

import type {
  ChatAttachment,
  ChatConversation,
  ChatHistoryMessage,
} from '@/apis/chatbotApis/types';

import { chatbotApis } from '@/apis/chatbotApis';
import { AiChatConfigContext } from '@/components/aiChat/config';
import { FINANCE_AI_CHAT } from '@/Finance/aiChatConfig';

// The HTML viewer is lazy-loaded and sandboxes the page in an iframe; here it just shows what it
// was given.
vi.mock('@/components/HtmlViewer', async () => {
  const { createElement } = await import('react');
  return {
    default: ({ content }: { content: string }) =>
      createElement('pre', { 'data-testid': 'html-preview' }, content),
  };
});

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

const uploaded = (id: number, fileName: string, truncated = false): ChatAttachment => ({
  id,
  fileName,
  kind: fileName.endsWith('.xlsx') ? 'excel' : 'text',
  charCount: 100,
  truncated,
});

const errorEnvelope = (status: number, code: string) =>
  new Response(JSON.stringify({ error: { code, message: 'server wording, never shown' } }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

// What the server holds, and what was asked of it.
let conversations: ChatConversation[];
let messagesOf: Record<string, ChatHistoryMessage[]>;
let chatReplies: Array<(init?: RequestInit) => Response | Promise<Response>>;
/** Conversations the panel asked the server to stop. */
let stops: string[];
/** Decisions the panel sent on writes waiting for the reader, by path. */
let approvals: Array<{ path: string; body: unknown }>;
let chatBodies: Array<{ message: string; conversationId: string; attachmentIds: number[] }>;
let listFetches: number;
let deleted: string[];
/** Answers to attachment uploads, in order. */
let uploadReplies: Array<() => Response | Promise<Response>>;
let uploads: number;
let deletedAttachments: string[];
/** Called while a chat turn is being answered, to let a test change the server's state first. */
let whileAnswering: (() => void) | undefined;

beforeEach(() => {
  conversations = [];
  messagesOf = {};
  chatReplies = [];
  chatBodies = [];
  listFetches = 0;
  deleted = [];
  uploadReplies = [];
  uploads = 0;
  deletedAttachments = [];
  stops = [];
  approvals = [];
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
        return chatReplies.shift()!(init);
      }
      const stop = url.match(/conversations\/([^/]+)\/stop$/);
      if (stop && method === 'POST') {
        stops.push(stop[1]);
        return json({ data: { stopped: true } });
      }
      const approval = url.match(/(conversations\/[^/]+\/approvals\/[^/]+)$/);
      if (approval && method === 'POST') {
        approvals.push({ path: approval[1], body: JSON.parse(init!.body as string) });
        return json({ data: { accepted: true } });
      }
      if (url.endsWith('/api/chatbot/attachments') && method === 'POST') {
        uploads += 1;
        return uploadReplies.shift()!();
      }
      if (url.includes('/api/chatbot/attachments/') && method === 'DELETE') {
        deletedAttachments.push(url.split('/').pop()!);
        return json({ data: null });
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
  // A menu still closing when its test ended is a portal left in the body; it must not be found
  // by the next test.
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

let collapses: number;
let activities: AssistantActivity[];
const lastActivity = () => activities[activities.length - 1];

/** The panel's owner holds the message being typed, as the one-line box does on the page. */
function Harness({ initialDraft }: { initialDraft: string }) {
  const [draft, setDraft] = useState(initialDraft);
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

async function mount(initialDraft = '') {
  collapses = 0;
  activities = [];
  const store = configureStore({
    reducer: { [chatbotApis.reducerPath]: chatbotApis.reducer },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(chatbotApis.middleware),
  });
  await act(async () =>
    root.render(
      <Provider store={store}>
        <AiChatConfigContext value={FINANCE_AI_CHAT}>
          <Harness initialDraft={initialDraft} />
        </AiChatConfigContext>
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

/** Waits for a conversation to be listed, then opens it. Waiting for its title's text is not
 *  enough: the greeting's suggestions ("分析台積電最近的走勢") can hold the same words, so the
 *  wait passes before the history has loaded. */
async function openConversation(title: string) {
  await waitFor(() => expect(row(title)).toBeDefined());
  await act(async () => row(title).click());
}

describe('AiAssistantPanel', () => {
  it('is a block with a header and a greeting, not a dialog that can be closed', async () => {
    await mount();

    expect(container.textContent).toContain('AI 助手');
    expect(container.textContent).toContain('今天想研究哪一檔？');
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
    expect(chatBodies).toEqual([
      { message: '2330 今天如何？', conversationId: '', attachmentIds: [] },
    ]);

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '好的' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await send('那 2317 呢？');
    await waitForText('好的');
    expect(chatBodies[1]).toEqual({
      message: '那 2317 呢？',
      conversationId: 'conv-1',
      attachmentIds: [],
    });
  });

  it('shows a failed reply inside the conversation, in its own words for the code it got', async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'error', code: 'context_too_long' }));
    await send('hi');

    await waitForText('錯誤: 這段對話太長了，請開新對話繼續。');
  });

  it('shows the tools an answer used above it, each closed once the answer is done', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'tool_use', name: 'get_realtime_quote' },
        { type: 'tool_use', name: 'get_stock_history' },
        { type: 'delta', text: '台積電今天收 1000 元' },
        { type: 'done', conversationId: 'c1' },
      ),
    );
    await send('台積電');

    await waitForText('台積電今天收 1000 元');
    const steps = [...container.querySelectorAll('[data-testid="tool-activity"] li')];
    expect(steps.map((li) => li.getAttribute('aria-label'))).toEqual([
      'realtime quote（完成）',
      'stock history（完成）',
    ]);
  });

  it('an answer that used no tool shows no tool list', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '你好' }, { type: 'done', conversationId: 'c1' }),
    );
    await send('hi');

    await waitForText('你好');
    expect(container.querySelector('[data-testid="tool-activity"]')).toBeNull();
  });

  it('asks before a write, sends the decision, and carries on in the same answer', async () => {
    await mount();
    const encoder = new TextEncoder();
    let push: (event: object) => void = () => {};
    let end: () => void = () => {};
    chatReplies.push(
      () =>
        new Response(
          new ReadableStream({
            start(controller) {
              push = (event) =>
                controller.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`));
              end = () => controller.close();
              push({ type: 'start', conversationId: 'conv-1' });
              push({
                type: 'approval_required',
                pendingId: 'p1',
                calls: [
                  {
                    toolUseId: 'tu1',
                    connector: 'site',
                    connectorName: '本站・單字庫',
                    tool: 'add_word',
                    displayName: '加入單字',
                    effect: 'write',
                    input: { word: 'serendipity' },
                  },
                ],
              });
            },
          }),
          { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
        ),
    );
    await send('把 serendipity 加入單字庫');
    await waitForText('需要你的確認');

    const allow = [...container.querySelectorAll('button')].find((b) => b.textContent === '允許')!;
    await act(async () => allow.click());
    await waitFor(() =>
      expect(approvals).toEqual([
        {
          path: 'conversations/conv-1/approvals/p1',
          body: { decisions: [{ toolUseId: 'tu1', approve: true }] },
        },
      ]),
    );

    await act(async () => {
      push({
        type: 'approval_resolved',
        pendingId: 'p1',
        decisions: [{ toolUseId: 'tu1', approve: true }],
      });
      push({ type: 'delta', text: '已加入 serendipity' });
      push({ type: 'done', conversationId: 'conv-1' });
      end();
    });
    await waitForText('已加入 serendipity');
    expect(container.textContent).toContain('已允許');
    expect([...container.querySelectorAll('button')].some((b) => b.textContent === '允許')).toBe(
      false,
    );
  });

  it("a decline is told in the app's own words when its config has them", async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'error', code: 'refused' }));
    await send('hi');

    await waitForText('錯誤: 這個問題我無法回答，請換個問法，或詢問台股相關的問題。');
  });

  it('a failure it has no words for still says the reply failed', async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'error', code: 'something_new' }));
    await send('hi');

    await waitForText('錯誤: 回覆失敗，請稍後再試。');
  });

  // The server keeps nothing of a reply that failed (TurnVerdict.Of), so reopening the conversation
  // shows what the bubble showed here: the error, not the cut-off answer.
  it('a reply that fails after some text shows only the error, dropping the cut-off answer', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '說到一半的分析' }, { type: 'error', code: 'busy' }),
    );
    await send('hi');

    await waitForText('錯誤: AI 服務目前忙碌中，請稍後再試。');
    expect(container.textContent).not.toContain('說到一半的分析');
  });

  it('says so under an answer that was cut short by the length limit', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'delta', text: '前半段的分析' },
        { type: 'done', conversationId: 'conv-1', truncated: true },
      ),
    );
    await send('分析 2330');

    await waitForText('前半段的分析');
    await waitForText('回覆因長度上限被截斷');
  });

  it('says nothing extra under a complete answer', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'delta', text: '完整答案' },
        { type: 'done', conversationId: 'conv-1', truncated: false },
      ),
    );
    await send('hi');

    await waitForText('完整答案');
    expect(container.textContent).not.toContain('截斷');
  });

  it('a request that cannot reach the server reads as the service being busy', async () => {
    await mount();
    chatReplies.push(() => Promise.reject(new TypeError('Failed to fetch')));
    await send('hi');

    await waitForText('錯誤: AI 服務目前忙碌中，請稍後再試。');
  });

  it('a message sent while a reply is being written waits, and goes once the reply is done', async () => {
    await mount();
    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () =>
            resolve(
              sseReply({ type: 'delta', text: '一' }, { type: 'done', conversationId: 'conv-1' }),
            );
        }),
    );
    await send('first');
    // The box still takes typing.
    expect(textarea().disabled).toBe(false);

    await send('second');
    expect(chatBodies).toHaveLength(1);
    expect(queuedTexts()).toEqual(['second']);

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '二' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await act(async () => finish());
    await waitFor(() => expect(chatBodies).toHaveLength(2));
    // Into the same conversation, the queue emptied.
    expect(chatBodies[1]).toEqual({
      message: 'second',
      conversationId: 'conv-1',
      attachmentIds: [],
    });
    await waitForText('二');
    expect(queuedTexts()).toEqual([]);
  });

  it('a waiting message can be taken back before it goes', async () => {
    await mount();
    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(sseReply({ type: 'done', conversationId: 'conv-1' }));
        }),
    );
    await send('first');
    await send('never mind');

    act(() => buttonByLabel('取消排隊')!.click());
    expect(queuedTexts()).toEqual([]);

    await act(async () => finish());
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());
    expect(chatBodies).toHaveLength(1);
  });

  it('停止 tells the server, and keeps what was written, marked as stopped', async () => {
    await mount();
    chatReplies.push((init) =>
      openReply(
        init?.signal,
        { type: 'start', conversationId: 'conv-1' },
        { type: 'delta', text: '台積電目前' },
      ),
    );
    await send('分析 2330');
    await waitForText('台積電目前');

    await act(async () => buttonByLabel('停止')!.click());

    // Two paragraphs of the bubble: the partial answer, then the mark.
    await waitFor(() => expect(container.textContent).toMatch(/台積電目前\s*（已中斷）/));
    expect(stops).toEqual(['conv-1']);
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());
    expect(container.textContent).not.toContain('錯誤');
  });

  it('stopping with a message waiting breaks in: the waiting message goes straight away', async () => {
    await mount();
    chatReplies.push((init) =>
      openReply(
        init?.signal,
        { type: 'start', conversationId: 'conv-1' },
        { type: 'delta', text: '長篇分析' },
      ),
    );
    await send('分析 2330');
    await waitForText('長篇分析');
    await send('改看 2317');

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '鴻海' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await act(async () => buttonByLabel('停止')!.click());

    await waitForText('鴻海');
    expect(chatBodies.map((b) => b.message)).toEqual(['分析 2330', '改看 2317']);
    expect(chatBodies[1].conversationId).toBe('conv-1');
  });

  it('stopping before the server has named a new conversation waits for the name, then stops it', async () => {
    await mount();
    let sendStart!: () => void;
    chatReplies.push((init) => {
      const encoder = new TextEncoder();
      return new Response(
        new ReadableStream({
          start(controller) {
            sendStart = () =>
              controller.enqueue(
                encoder.encode(
                  `data: ${JSON.stringify({ type: 'start', conversationId: 'conv-new' })}\n\n`,
                ),
              );
            init?.signal?.addEventListener('abort', () =>
              controller.error(new DOMException('aborted', 'AbortError')),
            );
          },
        }),
        { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
      );
    });
    await send('hi');
    await act(async () => buttonByLabel('停止')!.click());
    expect(stops).toEqual([]);

    await act(async () => sendStart());

    await waitFor(() => expect(stops).toEqual(['conv-new']));
    await waitForText('（已中斷）');
  });

  it('a suggestion under the greeting goes into the box, not straight to the assistant', async () => {
    await mount();
    const suggestion = [
      ...container.querySelectorAll<HTMLElement>('[data-testid="greeting-suggestions"] button'),
    ][0];

    // A real click moves the focus to the suggestion first; jsdom's click() does not.
    act(() => suggestion.focus());
    await act(async () => suggestion.click());

    expect(textarea().value).toBe(suggestion.textContent);
    expect(chatBodies).toEqual([]);
    // The cursor is back in the box, ready to send or change it.
    expect(document.activeElement).toBe(textarea());
  });

  it('opening puts the cursor after the message started in the one-line box', async () => {
    await mount('台積電的');
    expect(document.activeElement).toBe(textarea());
    expect([textarea().selectionStart, textarea().selectionEnd]).toEqual([4, 4]);
  });

  it('a message of only spaces is not sent', async () => {
    await mount();
    await send('   ');
    expect(chatBodies).toEqual([]);
  });

  it('the box empties as soon as a message goes', async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'conv-1' }));
    await send('hi');
    expect(textarea().value).toBe('');
  });

  it('Shift+Enter starts a new line instead of sending', async () => {
    await mount();
    act(() => {
      const setValue = Object.getOwnPropertyDescriptor(
        HTMLTextAreaElement.prototype,
        'value',
      )!.set!;
      setValue.call(textarea(), '第一行');
      textarea().dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      textarea().dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true }),
      );
    });
    expect(chatBodies).toEqual([]);
    expect(textarea().value).toBe('第一行');
  });

  it('taking back one waiting message leaves the others waiting', async () => {
    await mount();
    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(sseReply({ type: 'done', conversationId: 'conv-1' }));
        }),
    );
    await send('first');
    await send('second');
    await send('third');
    expect(queuedTexts()).toEqual(['second', 'third']);

    act(() => buttonByLabel('取消排隊')!.click());
    expect(queuedTexts()).toEqual(['third']);

    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'conv-1' }));
    await act(async () => finish());
    await waitFor(() => expect(chatBodies.map((b) => b.message)).toEqual(['first', 'third']));
  });

  it('an HTML answer opens in a preview, which follows it while it is still being written', async () => {
    await mount();
    let push!: (text: string) => void;
    let end!: () => void;
    chatReplies.push(() => {
      const encoder = new TextEncoder();
      return new Response(
        new ReadableStream({
          start(controller) {
            const event = (e: object) =>
              controller.enqueue(encoder.encode(`data: ${JSON.stringify(e)}\n\n`));
            event({ type: 'start', conversationId: 'conv-1' });
            push = (text) => event({ type: 'delta', text });
            end = () => {
              event({ type: 'done', conversationId: 'conv-1' });
              controller.close();
            };
          },
        }),
        { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
      );
    });
    await send('畫一張表');
    await act(async () => push('```html\n<p>第一列'));
    const previewButton = () =>
      [...container.querySelectorAll('button')].find((b) => b.textContent?.includes('預覽'));
    await waitFor(() => expect(previewButton()).toBeDefined());
    await act(async () => previewButton()!.click());

    const shown = () => document.querySelector('[data-testid="html-preview"]');
    await waitFor(() => expect(shown()?.textContent).toContain('第一列'));
    await act(async () => push('第二列</p>\n```'));
    await waitFor(() => expect(shown()?.textContent).toContain('第二列'));
    await act(async () => end());
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());

    // Starting over closes the preview of the old conversation.
    await act(async () => buttonByLabel('新對話')!.click());
    await waitFor(() => expect(shown()).toBeNull());
  });
});

/** The messages waiting their turn, as listed above the box. */
const queuedTexts = () =>
  [...container.querySelectorAll<HTMLElement>('[data-testid="queued-message"]')].map(
    (q) => q.querySelector('.MuiTypography-body2')!.textContent,
  );

/** A reply that streams these events and then stays open, until its request is dropped. */
const openReply = (signal: AbortSignal | null | undefined, ...events: object[]) =>
  new Response(
    new ReadableStream({
      start(controller) {
        const encoder = new TextEncoder();
        for (const event of events)
          controller.enqueue(encoder.encode(`data: ${JSON.stringify(event)}\n\n`));
        signal?.addEventListener('abort', () =>
          controller.error(new DOMException('aborted', 'AbortError')),
        );
      },
    }),
    { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
  );

/** Picks files with the 「+」 button's file input. */
async function attach(...files: File[]) {
  const input = container.querySelector<HTMLInputElement>(
    'input[data-testid="chat-attachment-input"]',
  )!;
  await act(async () => {
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
}

const file = (name: string, size = 10) => new File(['x'.repeat(size)], name);

/** An item of the 「+」 menu, opening the menu first. */
function menuItem(label: string) {
  if (!document.querySelector('[role="menu"]')) act(() => buttonByLabel('附加')!.click());
  return [...document.querySelectorAll<HTMLElement>('[role="menu"] li')].find((li) =>
    li.textContent?.startsWith(label),
  )!;
}

/** Pastes into the message box the way a browser fires it; says whether the panel took it over. */
async function paste(data: { text?: string; html?: string; files?: File[] }) {
  const event = new Event('paste', { bubbles: true, cancelable: true }) as Event & {
    clipboardData: Pick<DataTransfer, 'getData' | 'files'>;
  };
  event.clipboardData = {
    getData: (type: string) =>
      (type === 'text/plain' ? data.text : type === 'text/html' ? data.html : '') ?? '',
    files: (data.files ?? []) as unknown as FileList,
  };
  await act(async () => {
    textarea().dispatchEvent(event);
  });
  return event.defaultPrevented;
}

/** The attachment chips in the message box, or (given `within`) in a bubble. */
const chips = (within: ParentNode = container) =>
  [...within.querySelectorAll<HTMLElement>('.MuiChip-root')]
    .filter((c) => !c.closest('[data-testid="greeting-suggestions"]'))
    .map((c) => c.textContent);

const uploadsSettled = () =>
  waitFor(() =>
    expect(container.querySelector('.MuiChip-root .MuiCircularProgress-root')).toBeNull(),
  );

describe('AiAssistantPanel — attachments', () => {
  it('uploads a picked file, then sends its id with the message and shows it on the bubble', async () => {
    await mount();
    uploadReplies.push(() => json({ data: uploaded(7, '持股.xlsx') }));
    await attach(file('持股.xlsx'));
    await uploadsSettled();
    expect(chips()).toEqual(['持股.xlsx']);

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '你持有台積電' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await send('幫我看這份持股');
    await waitForText('你持有台積電');

    expect(chatBodies).toEqual([
      { message: '幫我看這份持股', conversationId: '', attachmentIds: [7] },
    ]);
    // Gone from the box, now on the sent message.
    const bubble = [
      ...container.querySelectorAll<HTMLElement>('[data-testid="user-message"]'),
    ].find((p) => p.textContent?.includes('幫我看這份持股') && !p.textContent.includes('你持有'))!;
    expect(chips(bubble)).toEqual(['持股.xlsx']);
    expect(chips()).toHaveLength(1);
  });

  it('does not send while a file is still uploading', async () => {
    await mount();
    let finish!: () => void;
    uploadReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(json({ data: uploaded(8, 'notes.txt') }));
        }),
    );
    await attach(file('notes.txt'));

    await send('看一下');
    expect(chatBodies).toHaveLength(0);

    await act(async () => finish());
    await uploadsSettled();
    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'conv-1' }));
    await act(async () => {
      textarea().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });
    await waitFor(() => expect(chatBodies).toHaveLength(1));
    expect(chatBodies[0].attachmentIds).toEqual([8]);
  });

  it('turns down a file type it cannot read without uploading it', async () => {
    await mount();
    await attach(file('slides.pptx'));

    expect(uploads).toBe(0);
    expect(container.textContent).toContain('slides.pptx：不支援這種檔案');
  });

  it('turns down a file over 10 MB without uploading it', async () => {
    await mount();
    await attach(file('big.csv', 10 * 1024 * 1024 + 1));

    expect(uploads).toBe(0);
    expect(container.textContent).toContain('big.csv：檔案太大，上限 10 MB。');
  });

  it('says in its own words why the server could not read a file', async () => {
    await mount();
    uploadReplies.push(() => errorEnvelope(400, 'AttachmentUnreadable'));
    await attach(file('broken.docx'));

    await waitForText('broken.docx：讀不出這個檔案的內容');
    expect(container.textContent).not.toContain('server wording');
  });

  it('takes at most five files for one message', async () => {
    await mount();
    for (let i = 1; i <= 5; i++) uploadReplies.push(() => json({ data: uploaded(i, `${i}.txt`) }));
    await attach(...[1, 2, 3, 4, 5, 6].map((i) => file(`${i}.txt`)));

    await waitForText('6.txt：一次最多附加 5 個檔案。');
    expect(uploads).toBe(5);
    await uploadsSettled();
    expect(menuItem('附加檔案').getAttribute('aria-disabled')).toBe('true');
  });

  it('a file turned down does not take one of the five places', async () => {
    await mount();
    for (let i = 1; i <= 5; i++) uploadReplies.push(() => json({ data: uploaded(i, `${i}.txt`) }));
    await attach(file('slides.pptx'));
    await attach(...[1, 2, 3, 4, 5].map((i) => file(`${i}.txt`)));

    await uploadsSettled();
    expect(uploads).toBe(5);
    expect(container.textContent).not.toContain('一次最多附加 5 個檔案');
  });

  it('nor does one turned down in the same pick: five good files still go, a sixth does not', async () => {
    await mount();
    for (let i = 1; i <= 5; i++) uploadReplies.push(() => json({ data: uploaded(i, `${i}.txt`) }));
    await attach(
      file('slides.pptx'),
      file('1.txt'),
      file('big.csv', 10 * 1024 * 1024 + 1),
      ...[2, 3, 4, 5, 6].map((i) => file(`${i}.txt`)),
    );

    await waitForText('6.txt：一次最多附加 5 個檔案。');
    await uploadsSettled();
    expect(uploads).toBe(5);
    expect(container.textContent).toContain('slides.pptx：不支援這種檔案');
    expect(container.textContent).toContain('big.csv：檔案太大');
    for (const name of ['1.txt', '2.txt', '3.txt', '4.txt', '5.txt'])
      expect(container.textContent).not.toContain(`${name}：`);
  });

  it('a file removed while it is still uploading is deleted on the server once it lands', async () => {
    await mount();
    let finish!: () => void;
    uploadReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(json({ data: uploaded(8, 'notes.txt') }));
        }),
    );
    await attach(file('notes.txt'));
    await act(async () => {
      container
        .querySelector('.MuiChip-root .MuiChip-deleteIcon')!
        .dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(chips()).toEqual([]);

    await act(async () => finish());
    await waitFor(() => expect(deletedAttachments).toEqual(['8']));
    expect(chips()).toEqual([]);
  });

  it('an image the browser cannot read says so, and is not uploaded', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn().mockRejectedValue(new Error('bad image')));
    await mount();
    await attach(new File(['x'], '壞掉.png', { type: 'image/png' }));

    await waitForText('壞掉.png：讀不出這個檔案的內容');
    expect(uploads).toBe(0);
  });

  describe('local previews of images', () => {
    let made: string[];
    let revoked: string[];
    const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
    beforeEach(() => {
      made = [];
      revoked = [];
      URL.createObjectURL = vi.fn(() => {
        made.push(`blob:${made.length + 1}`);
        return made[made.length - 1];
      });
      URL.revokeObjectURL = vi.fn((url: string) => void revoked.push(url));
    });
    afterEach(() => {
      URL.createObjectURL = original.create;
      URL.revokeObjectURL = original.revoke;
    });

    const localPictures = () =>
      [...container.querySelectorAll('.MuiChip-root img')].filter((img) =>
        img.getAttribute('src')?.startsWith('blob:'),
      );

    it('an image turned down gets no local preview', async () => {
      await mount();
      await attach(new File(['x'.repeat(10 * 1024 * 1024 + 1)], '大圖.png', { type: 'image/png' }));
      expect(container.textContent).toContain('大圖.png：檔案太大');
      expect(made).toEqual([]);
    });

    it('an uploaded image shows its local picture until the message goes', async () => {
      await mount();
      uploadReplies.push(() => json({ data: { ...uploaded(31, '走勢.png'), kind: 'image' } }));
      await attach(new File(['x'], '走勢.png', { type: 'image/png' }));
      await uploadsSettled();
      expect(localPictures().map((img) => img.getAttribute('src'))).toEqual(['blob:1']);

      chatReplies.push(() => sseReply({ type: 'done', conversationId: 'conv-1' }));
      await send('這張圖');
      expect(revoked).toEqual(['blob:1']);
    });

    it('an image whose upload fails lets its local picture go', async () => {
      await mount();
      uploadReplies.push(() => errorEnvelope(400, 'AttachmentUnreadable'));
      await attach(new File(['x'], '走勢.png', { type: 'image/png' }));
      await waitForText('走勢.png：讀不出這個檔案的內容');
      expect(localPictures()).toHaveLength(0);
      expect(revoked).toEqual(['blob:1']);
    });
  });

  it('a PDF says how many pages it has and opens from its chip', async () => {
    await mount();
    uploadReplies.push(() =>
      json({ data: { ...uploaded(11, '年報.pdf'), kind: 'pdf', pageCount: 12 } }),
    );
    await attach(file('年報.pdf'));
    await uploadsSettled();

    const chip = container.querySelector<HTMLAnchorElement>('a.MuiChip-root')!;
    expect(chip.getAttribute('href')).toBe('/api/chatbot/attachments/11/content');
    expect(chip.getAttribute('title')).toBe('年報.pdf（12 頁）');
  });

  it('a sent image shows as a picture from the server, and opens from its chip', async () => {
    conversations = [conversation('c1', '看圖')];
    messagesOf = {
      c1: [
        {
          ...turn(1, 'user', '這張圖'),
          attachments: [{ ...uploaded(5, '走勢.png'), kind: 'image' }],
        },
        turn(2, 'assistant', '看到了'),
      ],
    };
    await mount();
    await openConversation('看圖');
    await waitForText('看到了');

    const chip = container.querySelector<HTMLAnchorElement>('a.MuiChip-root')!;
    expect(chip.getAttribute('href')).toBe('/api/chatbot/attachments/5/content');
    expect(chip.querySelector('img')!.getAttribute('src')).toBe(
      '/api/chatbot/attachments/5/content',
    );
  });
});

describe('AiAssistantPanel — pasting', () => {
  beforeEach(() => {
    try {
      localStorage.clear();
    } catch {
      // No storage here: the setting starts off either way.
    }
  });

  it('a pasted screenshot is attached, named for when it was pasted', async () => {
    await mount();
    uploadReplies.push(() => json({ data: { ...uploaded(21, 'x.png'), kind: 'image' } }));

    const handled = await paste({ files: [new File(['png'], 'image.png', { type: 'image/png' })] });

    expect(handled).toBe(true);
    expect(uploads).toBe(1);
    expect(chips()[0]).toMatch(/^貼上的圖片-\d{6}\.png$/);
  });

  it('plain text is left for the browser to paste', async () => {
    await mount();
    expect(await paste({ text: '2330', html: '<b>2330</b>' })).toBe(false);
  });

  it('with 保留格式 on, a pasted table goes into the box as a Markdown table', async () => {
    await mount();
    const toggle = menuItem('貼上時保留格式'); // opens the menu in its own act
    act(() => toggle.click());
    expect(menuItem('貼上時保留格式').getAttribute('aria-checked')).toBe('true');

    const handled = await paste({
      text: '代號\t名稱\n2330\t台積電',
      html: '<table><tr><td>代號</td><td>名稱</td></tr><tr><td>2330</td><td>台積電</td></tr></table>',
    });

    expect(handled).toBe(true);
    // Columns are padded to line up; what matters is the table.
    const value = textarea().value.replace(/ {2,}/g, ' ');
    // On lines of its own, so what is typed next is not part of the table.
    expect(value).toBe('| 代號 | 名稱 |\n| --- | --- |\n| 2330 | 台積電 |\n');
  });

  it('保留格式 is remembered in this browser, on and off', async () => {
    await mount();
    const turnOn = menuItem('貼上時保留格式');
    act(() => turnOn.click());
    expect(localStorage.getItem('aiAssistant.keepPasteFormatting')).toBe('1');
    const turnOff = menuItem('貼上時保留格式');
    act(() => turnOff.click());
    expect(localStorage.getItem('aiAssistant.keepPasteFormatting')).toBe('0');
  });

  it('pasted text too long for the box is attached as a file instead', async () => {
    await mount();
    uploadReplies.push(() => json({ data: uploaded(22, 'x.txt') }));

    const handled = await paste({ text: '很長的財報內容'.repeat(1000) });

    expect(handled).toBe(true);
    expect(textarea().value).toBe('');
    expect(uploads).toBe(1);
    expect(chips()[0]).toMatch(/^貼上的內容-\d{6}\.txt$/);
  });

  it('removing an uploaded file deletes it on the server and leaves it out of the message', async () => {
    await mount();
    uploadReplies.push(() => json({ data: uploaded(9, 'a.txt') }));
    await attach(file('a.txt'));
    await uploadsSettled();

    await act(async () => {
      container
        .querySelector('.MuiChip-root .MuiChip-deleteIcon')!
        .dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await waitFor(() => expect(deletedAttachments).toEqual(['9']));
    expect(chips()).toEqual([]);

    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'conv-1' }));
    await send('沒有附件了');
    await waitFor(() => expect(chatBodies).toHaveLength(1));
    expect(chatBodies[0].attachmentIds).toEqual([]);
  });

  it('shows the files that were sent with a message when an old conversation is opened', async () => {
    conversations = [conversation('c1', '持股分析')];
    messagesOf = {
      c1: [
        { ...turn(1, 'user', '看這份'), attachments: [uploaded(3, '持股.xlsx')] },
        turn(2, 'assistant', '好的'),
      ],
    };
    await mount();
    await openConversation('持股分析');
    await waitForText('好的');
    expect(chips()).toEqual(['持股.xlsx']);
  });
});

describe('AiAssistantPanel — copy and edit', () => {
  let copied: string[];
  beforeEach(() => {
    copied = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn(async (text: string) => void copied.push(text)) },
    });
  });

  /** A message's own button, found inside that message. */
  const actionOf = (testId: 'user-message' | 'assistant-message', text: string, label: string) =>
    buttonByLabel(
      label,
      [...container.querySelectorAll<HTMLElement>(`[data-testid="${testId}"]`)].find((m) =>
        m.textContent?.includes(text),
      )!,
    );

  /** Types into the message being edited. */
  const typeInEditor = (text: string) => {
    const editor = container.querySelector<HTMLTextAreaElement>('textarea[aria-label="編輯訊息"]')!;
    act(() => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(
        editor,
        text,
      );
      editor.dispatchEvent(new Event('input', { bubbles: true }));
    });
    return editor;
  };

  it('copies an answer as it was written, and says it did', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'delta', text: '**台積電**收 1,050' },
        { type: 'done', conversationId: 'c1' },
      ),
    );
    await send('2330？');
    await waitForText('收 1,050');
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());

    await act(async () => actionOf('assistant-message', '收 1,050', '複製')!.click());

    expect(copied).toEqual(['**台積電**收 1,050']);
    expect(actionOf('assistant-message', '收 1,050', '已複製')).not.toBeNull();
  });

  it('copies the reader’s own message too', async () => {
    await mount();
    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'c1' }));
    await send('2330 最近如何？');
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());

    await act(async () => actionOf('user-message', '2330 最近如何？', '複製')!.click());

    expect(copied).toEqual(['2330 最近如何？']);
  });

  it('an answer still being written has no buttons yet, and nothing can be edited meanwhile', async () => {
    await mount();
    let finish!: () => void;
    chatReplies.push(
      () =>
        new Promise<Response>((resolve) => {
          finish = () =>
            resolve(
              sseReply(
                { type: 'start', conversationId: 'c1', userMessageId: 7 },
                { type: 'delta', text: '寫好了' },
                { type: 'done', conversationId: 'c1' },
              ),
            );
        }),
    );
    await send('hi');
    expect(
      container.querySelector('[data-testid="assistant-message"] button[aria-label="複製"]'),
    ).toBeNull();
    expect(actionOf('user-message', 'hi', '編輯')).toBeNull();

    await act(async () => finish());
    await waitForText('寫好了');
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());
    expect(actionOf('assistant-message', '寫好了', '複製')).not.toBeNull();
    expect(actionOf('user-message', 'hi', '編輯')).not.toBeNull();
  });

  it('editing a message sends the new wording in its place, and what followed it goes', async () => {
    conversations = [conversation('c1', '台積電')];
    messagesOf = {
      c1: [
        turn(1, 'user', '台積電？'),
        turn(2, 'assistant', '收 1,050'),
        turn(3, 'user', '那鴻海？'),
        turn(4, 'assistant', '收 210'),
      ],
    };
    await mount();
    await openConversation('台積電');
    await waitForText('收 210');

    await act(async () => actionOf('user-message', '台積電？', '編輯')!.click());
    expect(
      container.querySelector<HTMLTextAreaElement>('textarea[aria-label="編輯訊息"]')!.value,
    ).toBe('台積電？');
    typeInEditor('聯發科？');

    chatReplies.push(() =>
      sseReply(
        { type: 'start', conversationId: 'c1', userMessageId: 5 },
        { type: 'delta', text: '收 1,400' },
        { type: 'done', conversationId: 'c1', assistantMessageId: 6 },
      ),
    );
    await act(async () => {
      [...container.querySelectorAll('button')].find((b) => b.textContent === '送出')!.click();
    });

    await waitForText('收 1,400');
    expect(chatBodies).toEqual([
      { message: '聯發科？', conversationId: 'c1', attachmentIds: [], editMessageId: 1 },
    ]);
    // The edited message and everything after it are gone; the new turn is all that follows.
    for (const gone of ['台積電？', '收 1,050', '那鴻海？', '收 210'])
      expect(container.textContent).not.toContain(gone);
  });

  it('a message sent in this visit can be edited too, once the server has named it', async () => {
    await mount();
    chatReplies.push(() =>
      sseReply(
        { type: 'start', conversationId: 'c1', userMessageId: 41 },
        { type: 'delta', text: '答' },
        { type: 'done', conversationId: 'c1', assistantMessageId: 42 },
      ),
    );
    await send('first');
    await waitForText('答');
    await waitFor(() => expect(buttonByLabel('停止')).toBeNull());

    await act(async () => actionOf('user-message', 'first', '編輯')!.click());
    const editor = typeInEditor('second');
    chatReplies.push(() => sseReply({ type: 'done', conversationId: 'c1' }));
    await act(async () => {
      editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });

    await waitFor(() => expect(chatBodies).toHaveLength(2));
    expect(chatBodies[1]).toEqual({
      message: 'second',
      conversationId: 'c1',
      attachmentIds: [],
      editMessageId: 41,
    });
  });

  it('an edited message keeps its files: the server re-attaches them, so no ids are sent', async () => {
    conversations = [conversation('c1', '持股')];
    messagesOf = {
      c1: [
        { ...turn(1, 'user', '看這份'), attachments: [uploaded(3, '持股.xlsx')] },
        turn(2, 'assistant', '好的'),
      ],
    };
    await mount();
    await openConversation('持股');
    await waitForText('好的');

    await act(async () => actionOf('user-message', '看這份', '編輯')!.click());
    typeInEditor('只看台股');
    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '台股部分' }, { type: 'done', conversationId: 'c1' }),
    );
    await act(async () => {
      [...container.querySelectorAll('button')].find((b) => b.textContent === '送出')!.click();
    });

    await waitForText('台股部分');
    expect(chatBodies).toEqual([
      { message: '只看台股', conversationId: 'c1', attachmentIds: [], editMessageId: 1 },
    ]);
    const bubble = [
      ...container.querySelectorAll<HTMLElement>('[data-testid="user-message"]'),
    ].find((m) => m.textContent?.includes('只看台股'))!;
    expect(chips(bubble)).toEqual(['持股.xlsx']);
  });

  it('Esc puts the message back as it was', async () => {
    conversations = [conversation('c1', '台積電')];
    messagesOf = { c1: [turn(1, 'user', '台積電？'), turn(2, 'assistant', '收 1,050')] };
    await mount();
    await openConversation('台積電');
    await waitForText('收 1,050');

    await act(async () => actionOf('user-message', '台積電？', '編輯')!.click());
    const editor = typeInEditor('改掉');
    await act(async () => {
      editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    });

    expect(container.querySelector('textarea[aria-label="編輯訊息"]')).toBeNull();
    expect(container.textContent).toContain('台積電？');
    expect(chatBodies).toEqual([]);
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

  it('an opened conversation counts as one, even with a single stored message', async () => {
    conversations = [conversation('c3', '只問了一句')];
    messagesOf = { c3: [turn(9, 'user', '只問了一句？')] };
    await mount();
    await openConversation('只問了一句');
    await waitFor(() => expect(lastActivity()).toEqual({ busy: false, hasConversation: true }));
  });

  it('lists the conversations beside the chat without anything having to be opened', async () => {
    await mount();
    await waitForText('ETF 比較');

    expect(container.textContent).toContain('台積電財報');
    expect(container.querySelector('.MuiDrawer-root')).toBeNull();
  });

  it('opens a conversation from the column and carries on in it', async () => {
    await mount();
    await openConversation('台積電財報');
    await waitForText('毛利率 59%');
    expect(container.textContent).toContain('台積電最新財報？');

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '營收呢？好的' }, { type: 'done', conversationId: 'c1' }),
    );
    await send('營收呢？');
    await waitForText('營收呢？好的');
    expect(chatBodies).toEqual([{ message: '營收呢？', conversationId: 'c1', attachmentIds: [] }]);
  });

  it('shows a conversation as soon as its first reply has come in, and keeps the list current', async () => {
    conversations = [];
    await mount();
    // The empty list is on screen once its fetch has been answered, not when it was made.
    await waitForText('還沒有任何對話');
    expect(listFetches).toBe(1);

    whileAnswering = () => {
      conversations = [conversation('conv-new', '剛問的問題')];
    };
    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '答案' }, { type: 'done', conversationId: 'conv-new' }),
    );
    await send('剛問的問題');

    // The words are in the sent message straight away; the row needs the list's refetch.
    await waitFor(() => expect(row('剛問的問題')?.classList.contains('Mui-selected')).toBe(true));
    expect(listFetches).toBeGreaterThan(1);
  });

  it('starts over with 新對話: the messages go and the next message carries no conversation id', async () => {
    await mount();
    await openConversation('台積電財報');
    await waitForText('毛利率 59%');

    act(() => buttonByLabel('新對話')!.click());
    expect(container.textContent).not.toContain('毛利率 59%');
    expect(container.textContent).toContain('今天想研究哪一檔？');

    chatReplies.push(() =>
      sseReply({ type: 'delta', text: '全新的' }, { type: 'done', conversationId: 'conv-2' }),
    );
    await send('第二則');
    await waitForText('全新的');
    expect(chatBodies).toEqual([{ message: '第二則', conversationId: '', attachmentIds: [] }]);
  });

  it('deleting the conversation that is open clears the chat as well', async () => {
    await mount();
    await openConversation('台積電財報');
    await waitForText('毛利率 59%');

    await act(async () => buttonByLabel('刪除對話', row('台積電財報'))!.click());

    await waitFor(() => expect(deleted).toEqual(['c1']));
    await waitFor(() => expect(container.textContent).not.toContain('毛利率 59%'));
    expect(container.textContent).toContain('今天想研究哪一檔？');
    await waitFor(() => expect(container.textContent).not.toContain('台積電財報'));
  });

  it('cannot be switched or restarted while a reply is being written', async () => {
    await mount();
    await openConversation('台積電財報');
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
