import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { describe, expect, it } from 'vitest';

import { ANSWERED_ANNOUNCEMENT, WORKING_ANNOUNCEMENT } from '../utils/announcement';

import { useChatAnnouncer } from './useChatAnnouncer';

import type { Message } from '../types';

// No testing library here: the hook rendered in a component that reports what it returned.
function renderHook<P>(hook: (props: P) => string, { initialProps }: { initialProps: P }) {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  const result = { current: '' };
  const root = createRoot(document.createElement('div'));
  const Probe = ({ props }: { props: P }) => {
    result.current = hook(props);
    return null;
  };
  const rerender = (props: P) => act(() => root.render(<Probe props={props} />));
  rerender(initialProps);
  return { result, rerender };
}

const msg = (over: Partial<Message>): Message => ({
  id: 'm',
  content: '',
  isUser: false,
  timestamp: new Date(0),
  ...over,
});

describe('useChatAnnouncer', () => {
  it('says nothing for a conversation opened as it is', () => {
    const { result } = renderHook(() => useChatAnnouncer([msg({ content: '舊的回答' })], false), {
      initialProps: {},
    });
    expect(result.current).toBe('');
  });

  it('says once that an answer started, not each piece, then that it is done', () => {
    const { result, rerender } = renderHook(({ m, busy }) => useChatAnnouncer(m, busy), {
      initialProps: { m: [msg({ isUser: true, content: '問題' })], busy: false },
    });
    rerender({ m: [msg({ isUser: true }), msg({ content: '一' })], busy: true });
    expect(result.current).toBe(WORKING_ANNOUNCEMENT);
    rerender({ m: [msg({ isUser: true }), msg({ content: '一二三' })], busy: true });
    expect(result.current).toBe(WORKING_ANNOUNCEMENT);
    rerender({
      m: [msg({ isUser: true }), msg({ content: '一二三', answered: true })],
      busy: false,
    });
    expect(result.current).toBe(ANSWERED_ANNOUNCEMENT);
  });

  it('says a write is waiting once its card arrives', () => {
    const approval = {
      state: 'pending' as const,
      request: {
        pendingId: 'p1',
        calls: [
          {
            toolUseId: 't',
            connector: 'site',
            tool: 'remember',
            displayName: '記住',
            effect: 'write' as const,
            input: {},
          },
        ],
      },
    };
    const { result, rerender } = renderHook(({ m }) => useChatAnnouncer(m, true), {
      initialProps: { m: [msg({ isUser: true })] },
    });
    rerender({ m: [msg({ isUser: true }), msg({ approval })] });
    expect(result.current).toContain('需要你的確認：記住');
  });
});
