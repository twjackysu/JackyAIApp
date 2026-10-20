import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { Message } from '../types';

import MessageArea from './MessageArea';

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
  act(() => root.render(<MessageArea messages={messages} onPreview={vi.fn()} active={active} />));

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
    expect(container.textContent).toContain('您好！我是您的 AI 助手');
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

  it('never scrolls the page itself', () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    render([message('1', 'hi', true), message('2', 'answer')]);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });
});
