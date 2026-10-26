import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { Message } from '../types';

import MessageArea from './MessageArea';

import { AiChatConfigContext } from '@/components/aiChat/config';
import { FINANCE_AI_CHAT } from '@/Finance/aiChatConfig';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** Which message contents were drawn, once per draw: the Markdown is the costly part of an earlier answer. */
const drawn = vi.hoisted(() => [] as string[]);
vi.mock('@/components/MarkdownMessage', () => ({
  default: ({ content }: { content: string }) => {
    drawn.push(content);
    return <div>{content}</div>;
  },
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  drawn.length = 0;
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const message = (id: string, content: string, isUser = false): Message => ({
  id,
  content,
  isUser,
  serverId: 1,
  timestamp: new Date(2026, 9, 20, 12),
});

/** Renders as a page does: a new callback each time, as an inline arrow would be. */
const render = (messages: Message[], busy = false, onFollowUp = vi.fn()) =>
  act(() =>
    root.render(
      <AiChatConfigContext value={FINANCE_AI_CHAT}>
        <MessageArea
          messages={messages}
          onPreview={() => {}}
          active
          busy={busy}
          onEdit={() => {}}
          onDecide={() => {}}
          onFollowUp={onFollowUp}
        />
      </AiChatConfigContext>,
    ),
  );

const earlier = [
  message('u1', '台積電最近怎麼樣？', true),
  message('a1', '## 台積電\n\n| 日期 | 收盤 |\n| --- | --- |\n| 10/23 | 1,050 |'),
  message('u2', '那聯發科呢？', true),
];

describe('MessageArea drawing', () => {
  it('draws only the answer being written as it streams, not the earlier messages', () => {
    render([...earlier, message('a2', '聯')], true);
    drawn.length = 0;

    render([...earlier, message('a2', '聯發科')], true);
    render([...earlier, message('a2', '聯發科最近')], true);

    expect(drawn).toEqual(['聯發科', '聯發科最近']);
  });

  it('draws nothing again while the page re-renders for something else (the reader typing)', () => {
    const messages = [...earlier, message('a2', '聯發科最近走勢偏強。')];
    render(messages);
    drawn.length = 0;

    render(messages);
    render(messages);

    expect(drawn).toEqual([]);
  });

  it('still calls the callback the page passed last', () => {
    const answered: Message = {
      ...message('a2', '聯發科最近走勢偏強。'),
      answered: true,
      toolSteps: [{ key: 's1', name: 'get_stock_history', label: '個股歷史股價', status: 'done' }],
    };
    const messages = [...earlier, answered];
    const first = vi.fn();
    const second = vi.fn();
    render(messages, false, first);
    render(messages, false, second);

    const followUp = container.querySelector<HTMLElement>('[data-testid="follow-ups"] button')!;
    act(() => followUp.click());

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledWith('它的三大法人最近在買還是賣？');
  });
});
