import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { Message } from '../types';

import MessageArea, { REVEAL_MS, type MessageReveal } from './MessageArea';

import { AiChatConfigContext } from '@/components/aiChat/config';
import { FINANCE_AI_CHAT } from '@/Finance/aiChatConfig';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** jsdom does no layout, so the box's scroll geometry is whatever the test says it is. */
const geometry = { scrollHeight: 1000, clientHeight: 400, scrollTop: 0 };
const originals = {
  scrollHeight: Object.getOwnPropertyDescriptor(Element.prototype, 'scrollHeight'),
  clientHeight: Object.getOwnPropertyDescriptor(Element.prototype, 'clientHeight'),
  scrollTop: Object.getOwnPropertyDescriptor(Element.prototype, 'scrollTop'),
};

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  Object.assign(geometry, { scrollHeight: 1000, clientHeight: 400, scrollTop: 0 });
  Object.defineProperty(Element.prototype, 'scrollHeight', {
    configurable: true,
    get: () => geometry.scrollHeight,
  });
  Object.defineProperty(Element.prototype, 'clientHeight', {
    configurable: true,
    get: () => geometry.clientHeight,
  });
  Object.defineProperty(Element.prototype, 'scrollTop', {
    configurable: true,
    get: () => geometry.scrollTop,
    set: (value: number) => {
      geometry.scrollTop = value;
    },
  });
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  for (const [name, original] of Object.entries(originals)) {
    if (original) Object.defineProperty(Element.prototype, name, original);
  }
});

const message = (id: string, content: string, isUser = false): Message => ({
  id,
  content,
  isUser,
  timestamp: new Date(2026, 9, 20, 12),
});

const render = (messages: Message[], active = true) =>
  act(() =>
    root.render(
      <AiChatConfigContext value={FINANCE_AI_CHAT}>
        <MessageArea messages={messages} onPreview={vi.fn()} active={active} />
      </AiChatConfigContext>,
    ),
  );

/** The reader drags the scrollbar to `top`. */
const readerScrollsTo = (top: number) => {
  geometry.scrollTop = top;
  act(() => {
    container.firstElementChild!.dispatchEvent(new Event('scroll'));
  });
};

describe('MessageArea', () => {
  it('shows a greeting while the conversation is empty', () => {
    render([]);
    expect(container.textContent).toContain('今天想研究哪一檔？');
  });

  it('follows a new message to the end', () => {
    render([message('1', 'hi', true)]);
    expect(geometry.scrollTop).toBe(1000);
  });

  it('keeps following a reply that grows while the reader is at the end', () => {
    render([message('1', 'hi', true), message('2', '')]);

    geometry.scrollHeight = 1400;
    render([message('1', 'hi', true), message('2', 'a long streamed answer')]);

    expect(geometry.scrollTop).toBe(1400);
  });

  it('does not drag a reader back down who scrolled up while the reply streams in', () => {
    render([message('1', 'hi', true), message('2', '')]);
    readerScrollsTo(100);

    geometry.scrollHeight = 1400;
    render([message('1', 'hi', true), message('2', 'a long streamed answer')]);

    expect(geometry.scrollTop).toBe(100);
  });

  it('still follows a brand new message after the reader scrolled up', () => {
    render([message('1', 'hi', true), message('2', 'answer')]);
    readerScrollsTo(100);

    geometry.scrollHeight = 1600;
    render([message('1', 'hi', true), message('2', 'answer'), message('3', 'and another', true)]);

    expect(geometry.scrollTop).toBe(1600);
  });

  it('follows again once the reader scrolls back to the end', () => {
    render([message('1', 'hi', true), message('2', '')]);
    readerScrollsTo(100);
    readerScrollsTo(1000 - 400 - 10); // within reach of the end

    geometry.scrollHeight = 1400;
    render([message('1', 'hi', true), message('2', 'more text')]);

    expect(geometry.scrollTop).toBe(1400);
  });

  it('leaves the scroll position alone while its tab is hidden, and catches up when shown', () => {
    render([message('1', 'hi', true)], false);
    expect(geometry.scrollTop).toBe(0);

    render([message('1', 'hi', true), message('2', 'answer arrived meanwhile')], false);
    expect(geometry.scrollTop).toBe(0);

    render([message('1', 'hi', true), message('2', 'answer arrived meanwhile')], true);
    expect(geometry.scrollTop).toBe(1000);
  });

  describe('a write waiting for the reader', () => {
    const asking = (state: 'pending' | 'resolved', content: string): Message => ({
      ...message('2', content),
      approval: {
        request: {
          pendingId: 'p1',
          calls: [
            {
              toolUseId: 't1',
              connector: 'site',
              tool: 'add_word_to_repository',
              displayName: '加入單字庫',
              effect: 'write',
              input: { word: 'serendipity' },
            },
          ],
        },
        state,
        decisions: state === 'resolved' ? [{ toolUseId: 't1', approve: true }] : undefined,
      },
    });
    /** Whether the card comes after the answer's text in the page. */
    const cardAfterText = () => {
      const card = container.querySelector('[aria-label="需要你的確認"]')!;
      const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
      let text: Node | null = walker.nextNode();
      while (text && !text.textContent?.includes('請確認')) text = walker.nextNode();
      return Boolean(text!.compareDocumentPosition(card) & Node.DOCUMENT_POSITION_FOLLOWING);
    };

    it('asks below the text that leads up to it', () => {
      render([message('1', 'hi', true), asking('pending', '我幫你加入單字庫，請確認：')]);
      expect(cardAfterText()).toBe(true);
    });

    it('shows no thinking dots while the turn waits on the reader', () => {
      render([message('1', 'hi', true), asking('pending', '')]);
      expect(container.querySelector('[aria-label="需要你的確認"]')).not.toBeNull();
      expect(container.querySelector('[aria-label="AI 助手思考中"]')).toBeNull();
    });

    it('once answered, stays above the text as a record of what ran', () => {
      render([message('1', 'hi', true), asking('resolved', '請確認：……已加入。')]);
      expect(cardAfterText()).toBe(false);
    });
  });

  it('says over a message another assistant handed over where it came from, and over no other', () => {
    render([
      { ...message('u1', '幫我做一個業績總覽 dashboard', true), handedOverBy: 'Studio AI' },
      message('a1', '好的'),
      message('u2', '再加一張圖', true),
    ]);
    const marks = container.querySelectorAll('[data-testid="handed-over"]');
    expect(marks).toHaveLength(1);
    expect(marks[0].textContent).toBe('由 Studio AI 轉來');
    const users = container.querySelectorAll('[data-testid="user-message"]');
    expect(users[0].contains(marks[0])).toBe(true);
  });

  const renderOpen = (
    messages: Message[],
    conversationId: string,
    reveal: MessageReveal | null = null,
  ) =>
    act(() =>
      root.render(
        <AiChatConfigContext value={FINANCE_AI_CHAT}>
          <MessageArea
            messages={messages}
            onPreview={vi.fn()}
            active
            conversationId={conversationId}
            reveal={reveal}
          />
        </AiChatConfigContext>,
      ),
    );
  const stored = (serverId: number, content: string, isUser = false): Message => ({
    ...message(`s${serverId}`, content, isUser),
    serverId,
  });

  it('goes to the end of another conversation opened, even one as long as where the reader scrolled up', () => {
    renderOpen([stored(1, 'hi', true), stored(2, 'answer')], 'c1');
    readerScrollsTo(100);

    geometry.scrollHeight = 1200;
    renderOpen([stored(3, 'other', true), stored(4, 'other answer')], 'c2');

    expect(geometry.scrollTop).toBe(1200);
  });

  it('lights a found message again for the whole time when the same find is opened again (D-193)', () => {
    vi.useFakeTimers();
    try {
      const messages = [stored(1, 'q', true), stored(2, '找到的那句')];
      renderOpen(messages, 'c1', { messageId: 2 });
      const lit = () =>
        [...container.querySelectorAll('[data-revealed]')].map((m) => m.textContent);
      expect(lit()).toEqual([expect.stringContaining('找到的那句')]);

      act(() => vi.advanceTimersByTime(REVEAL_MS - 400));
      renderOpen(messages, 'c1', { messageId: 2 });
      // The first opening's timer must not put the second one out early.
      act(() => vi.advanceTimersByTime(600));
      expect(lit()).toHaveLength(1);
      act(() => vi.advanceTimersByTime(REVEAL_MS));
      expect(lit()).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  describe('content drawn after the scroll was set (D-197)', () => {
    /** jsdom has no ResizeObserver: this one lets the test say when the column changed size. */
    let resized: (() => void) | null = null;
    const original = globalThis.ResizeObserver;
    beforeEach(() => {
      resized = null;
      globalThis.ResizeObserver = class {
        constructor(callback: ResizeObserverCallback) {
          resized = () => act(() => callback([], this as unknown as ResizeObserver));
        }
        observe() {}
        unobserve() {}
        disconnect() {
          resized = null;
        }
      } as unknown as typeof ResizeObserver;
    });
    afterEach(() => {
      globalThis.ResizeObserver = original;
    });

    /** Where the found message now sits, as the box measures it. */
    const foundAt = (top: number) => {
      // The second message, as the column (the box's one child) holds one element per message.
      const found = container.firstElementChild!.firstElementChild!.children[1] as HTMLElement;
      found.getBoundingClientRect = () => ({ top }) as DOMRect;
    };

    it('keeps following the end while something above it grows', () => {
      render([message('1', 'hi', true), message('2', 'answer')]);
      geometry.scrollHeight = 1500;
      resized!();
      expect(geometry.scrollTop).toBe(1500);
    });

    it('leaves what grows once the reader pressed something in the conversation where it is', () => {
      render([message('1', 'hi', true), message('2', 'answer')]);
      act(() => {
        container.firstElementChild!.dispatchEvent(new Event('pointerdown'));
      });
      const at = geometry.scrollTop;
      geometry.scrollHeight = 1500;
      resized!();
      expect(geometry.scrollTop).toBe(at);

      // A new message follows again, and so does what grows in it.
      render([message('1', 'hi', true), message('2', 'answer'), message('3', 'more', true)]);
      geometry.scrollHeight = 1800;
      resized!();
      expect(geometry.scrollTop).toBe(1800);
    });

    it('follows again what an answer draws as it ends, after a press while it was written', () => {
      const messages = [message('1', 'hi', true), message('2', 'answer')];
      act(() =>
        root.render(
          <AiChatConfigContext value={FINANCE_AI_CHAT}>
            <MessageArea messages={messages} onPreview={vi.fn()} active busy />
          </AiChatConfigContext>,
        ),
      );
      act(() => {
        container.firstElementChild!.dispatchEvent(new Event('pointerdown'));
      });
      act(() =>
        root.render(
          <AiChatConfigContext value={FINANCE_AI_CHAT}>
            <MessageArea messages={messages} onPreview={vi.fn()} active busy={false} />
          </AiChatConfigContext>,
        ),
      );
      geometry.scrollHeight = 1500;
      resized!();
      expect(geometry.scrollTop).toBe(1500);
    });

    it('leaves a reader who scrolled up where they are', () => {
      render([message('1', 'hi', true), message('2', 'answer')]);
      readerScrollsTo(100);
      geometry.scrollHeight = 1500;
      resized!();
      expect(geometry.scrollTop).toBe(100);
    });

    it('keeps a found message in place when what is above it grows', () => {
      renderOpen([stored(1, 'q', true), stored(2, '找到的那句'), stored(3, 'later')], 'c1', {
        messageId: 2,
      });
      const after = geometry.scrollTop;
      foundAt(316);
      resized!();
      expect(geometry.scrollTop).toBe(after + 300);
    });

    it('lets go of the found message once the reader scrolls, or the time is up', () => {
      vi.useFakeTimers();
      try {
        const messages = [stored(1, 'q', true), stored(2, '找到的那句'), stored(3, 'later')];
        renderOpen(messages, 'c1', { messageId: 2 });
        const box = container.firstElementChild!;
        act(() => {
          box.dispatchEvent(new Event('wheel'));
        });
        const after = geometry.scrollTop;
        foundAt(316);
        resized!();
        expect(geometry.scrollTop).toBe(after);

        renderOpen(messages, 'c1', { messageId: 2 });
        act(() => vi.advanceTimersByTime(REVEAL_MS + 1));
        const later = geometry.scrollTop;
        foundAt(500);
        resized!();
        expect(geometry.scrollTop).toBe(later);
      } finally {
        vi.useRealTimers();
      }
    });
  });

  it('never scrolls the page itself', () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    render([message('1', 'hi', true), message('2', 'answer')]);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });
});
