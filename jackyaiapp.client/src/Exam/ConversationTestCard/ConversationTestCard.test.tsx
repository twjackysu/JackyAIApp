import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ConversationTestCard from '.';

const mocks = vi.hoisted(() => ({
  start: vi.fn(),
  respond: vi.fn(),
  summarize: vi.fn(),
  summaryState: { data: undefined, isLoading: false, error: undefined as unknown },
}));

vi.mock('@/apis/examApis', () => ({
  useStartConversationTestMutation: () => [mocks.start, { isLoading: false, error: undefined }],
  useRespondToConversationMutation: () => [mocks.respond, { isLoading: false }],
  useSummarizeConversationMutation: () => [
    mocks.summarize,
    { ...mocks.summaryState, reset: vi.fn() },
  ],
}));

// The scenario picker and the microphone are not what is tested here.
vi.mock('./components/ScenarioSelector', () => ({
  default: ({ onSelectScenario }: { onSelectScenario: (...a: unknown[]) => void }) => (
    <button onClick={() => onSelectScenario('At a café', 'customer', 'barista', 2)}>start</button>
  ),
}));
vi.mock('./hooks', () => ({
  useAutoScroll: () => ({ current: null }),
  useVoiceRecording: () => ({
    isRecording: false,
    isTranscribing: false,
    mediaRecorder: null,
    recordingNote: null,
    toggleRecording: vi.fn(),
  }),
}));
vi.mock('./components/ConversationSummary', () => ({
  default: ({ open, error }: { open: boolean; error?: string | null }) =>
    open ? <div data-testid="summary">{error ?? ''}</div> : null,
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;

const box = () => container.querySelector('textarea:not([aria-hidden])') as HTMLTextAreaElement;
const alertText = () => container.querySelector('[role="alert"]')?.textContent ?? null;
const button = (text: string) =>
  [...container.querySelectorAll('button')].find((b) => b.textContent === text)!;

function type(text: string) {
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(box(), text);
    box().dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function send() {
  await act(async () => {
    box().dispatchEvent(
      new KeyboardEvent('keypress', { key: 'Enter', charCode: 13, bubbles: true }),
    );
  });
}

describe('ConversationTestCard', () => {
  beforeEach(async () => {
    mocks.start.mockReset().mockReturnValue({
      unwrap: () =>
        Promise.resolve({
          data: {
            scenario: 'At a café',
            userRole: 'customer',
            aiRole: 'barista',
            firstMessage: 'Hi! What can I get you?',
          },
        }),
    });
    mocks.respond.mockReset();
    mocks.summarize.mockReset();
    mocks.summaryState.error = undefined;
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    act(() =>
      root.render(
        <MemoryRouter>
          <ConversationTestCard />
        </MemoryRouter>,
      ),
    );
    await act(async () => button('start').click());
  });
  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('takes a message with no reply back out of the transcript and into the box, saying why', async () => {
    mocks.respond.mockReturnValue({
      unwrap: () =>
        Promise.reject({ status: 503, data: { error: { message: 'AI 服務暫時忙碌。' } } }),
    });
    type('A latte, please.');

    await send();

    expect(box().value).toBe('A latte, please.');
    expect(alertText()).toContain('AI 服務暫時忙碌。');
    expect(alertText()).toContain('已放回輸入框');
    expect(button('結束並看總結').disabled).toBe(true);

    // Sending again goes with the history as it was: no unanswered turn in it.
    mocks.respond.mockReturnValue({
      unwrap: () =>
        Promise.resolve({
          data: { aiResponse: 'Sure, coming up.', correction: { hasCorrection: false } },
        }),
    });
    await send();

    const request = mocks.respond.mock.calls[1][0];
    expect(request.userMessage).toBe('A latte, please.');
    expect(request.conversationHistory).toHaveLength(1);
    expect(request.conversationContext.turnNumber).toBe(1);
    expect(alertText()).toBeNull();
    expect(container.textContent).toContain('Sure, coming up.');
  });

  it('keeps what was typed while the reply was pending, and says the earlier one was not sent', async () => {
    let fail: (e: unknown) => void = () => {};
    mocks.respond.mockReturnValue({
      unwrap: () => new Promise((_, reject) => (fail = reject)),
    });
    type('A latte, please.');
    await send();
    type('Make it large.');

    await act(async () => fail(new Error('boom')));

    expect(box().value).toBe('Make it large.');
    expect(alertText()).toContain('剛才那句沒有送出。');
    expect(alertText()).not.toContain('已放回輸入框');
  });

  it('says so in its own words when the server gives no reason', async () => {
    mocks.respond.mockReturnValue({ unwrap: () => Promise.reject(new Error('boom')) });
    type('Hello');

    await send();

    expect(alertText()).toContain('對方沒有回應。');
  });

  it('shows why a summary could not be made', async () => {
    mocks.respond.mockReturnValue({
      unwrap: () =>
        Promise.resolve({ data: { aiResponse: 'Sure.', correction: { hasCorrection: false } } }),
    });
    type('Hello');
    await send();
    mocks.summaryState.error = { status: 500, data: { error: { message: '總結服務錯誤' } } };
    mocks.summarize.mockReturnValue({ unwrap: () => Promise.reject(mocks.summaryState.error) });

    await act(async () => button('結束並看總結').click());

    expect(container.querySelector('[data-testid="summary"]')?.textContent).toBe('總結服務錯誤');
  });
});
