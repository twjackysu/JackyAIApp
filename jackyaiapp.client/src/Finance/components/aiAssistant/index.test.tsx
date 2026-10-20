import { configureStore } from '@reduxjs/toolkit';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import AiAssistantPanel from '.';

import { chatbotApis } from '@/apis/chatbotApis';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

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

const bodyOf = (call: unknown[]) => JSON.parse((call[1] as RequestInit).body as string);

let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal('fetch', fetchMock);

  const store = configureStore({
    reducer: { [chatbotApis.reducerPath]: chatbotApis.reducer },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(chatbotApis.middleware),
  });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  act(() =>
    root.render(
      <Provider store={store}>
        <AiAssistantPanel active />
      </Provider>,
    ),
  );
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

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

const waitForText = (text: string) =>
  act(async () => {
    await vi.waitFor(() => expect(container.textContent).toContain(text));
  });

const buttonByLabel = (label: string) =>
  container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);

describe('AiAssistantPanel', () => {
  it('is a block with a header and a greeting, not a dialog that can be closed', () => {
    expect(container.textContent).toContain('AI 助手');
    expect(container.textContent).toContain('您好！我是您的 AI 助手');
    expect(buttonByLabel('對話紀錄')).not.toBeNull();
    expect(buttonByLabel('新對話')).not.toBeNull();
    expect(buttonByLabel('關閉')).toBeNull();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.querySelector('.MuiFab-root')).toBeNull();
  });

  it('sends the message, shows the streamed reply, and continues the same conversation', async () => {
    fetchMock.mockImplementationOnce(async () =>
      sseReply(
        { type: 'delta', text: '台積電' },
        { type: 'delta', text: '今天收 1000 元' },
        { type: 'done', conversationId: 'conv-1' },
      ),
    );
    await send('2330 今天如何？');

    await waitForText('台積電今天收 1000 元');
    expect(container.textContent).toContain('2330 今天如何？');
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/chatbot/chat');
    expect(bodyOf(fetchMock.mock.calls[0])).toEqual({
      message: '2330 今天如何？',
      conversationId: '',
    });

    fetchMock.mockImplementationOnce(async () =>
      sseReply({ type: 'delta', text: '好的' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await send('那 2317 呢？');
    await waitForText('好的');
    expect(bodyOf(fetchMock.mock.calls[1])).toEqual({
      message: '那 2317 呢？',
      conversationId: 'conv-1',
    });
  });

  it('shows a failed reply inside the conversation instead of leaving an empty bubble', async () => {
    fetchMock.mockImplementationOnce(async () =>
      sseReply({ type: 'error', message: '額度已用完' }),
    );
    await send('hi');

    await waitForText('錯誤: 額度已用完');
  });

  it('starts over with 新對話: the messages go and the next message carries no conversation id', async () => {
    fetchMock.mockImplementationOnce(async () =>
      sseReply({ type: 'delta', text: '第一則回覆' }, { type: 'done', conversationId: 'conv-1' }),
    );
    await send('第一則');
    await waitForText('第一則回覆');

    act(() => buttonByLabel('新對話')!.click());
    expect(container.textContent).not.toContain('第一則回覆');
    expect(container.textContent).toContain('您好！我是您的 AI 助手');

    fetchMock.mockImplementationOnce(async () =>
      sseReply({ type: 'delta', text: '全新的' }, { type: 'done', conversationId: 'conv-2' }),
    );
    await send('第二則');
    await waitForText('全新的');
    expect(bodyOf(fetchMock.mock.calls[1])).toEqual({ message: '第二則', conversationId: '' });
  });

  it('ignores Enter while a reply is still being written', async () => {
    let finish!: () => void;
    fetchMock.mockImplementationOnce(
      () =>
        new Promise<Response>((resolve) => {
          finish = () => resolve(sseReply({ type: 'done', conversationId: 'conv-1' }));
        }),
    );
    await send('first');
    expect(textarea().disabled).toBe(true);

    await send('second');
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await act(async () => finish());
  });
});
