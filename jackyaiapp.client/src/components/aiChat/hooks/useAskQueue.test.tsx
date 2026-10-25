import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { useAskQueue } from './useAskQueue';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let container: HTMLDivElement;
let root: Root;
const result: { current?: ReturnType<typeof useAskQueue> } = {};
const queue = {
  get ask() {
    return result.current!.ask;
  },
  enqueue: (...args: Parameters<ReturnType<typeof useAskQueue>['enqueue']>) =>
    result.current!.enqueue(...args),
  sent: (id: number) => result.current!.sent(id),
};

function Probe() {
  result.current = useAskQueue();
  return null;
}

beforeEach(async () => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root.render(<Probe />));
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe('useAskQueue', () => {
  it('hands on the first ask until it is sent, then the next, each with an id of its own', async () => {
    expect(queue.ask).toBeUndefined();
    await act(async () => {
      queue.enqueue({ text: '解釋「本益比」', fresh: true, quotesPageText: true });
      queue.enqueue({ text: '給我今天的簡報' });
    });
    expect(queue.ask).toEqual({ id: 1, text: '解釋「本益比」', fresh: true, quotesPageText: true });

    await act(async () => queue.sent(1));
    expect(queue.ask).toEqual({ id: 2, text: '給我今天的簡報' });
    await act(async () => queue.sent(2));
    expect(queue.ask).toBeUndefined();
  });

  it('drops only the ask that was sent, and keeps counting ids after the queue empties', async () => {
    await act(async () => queue.enqueue({ text: 'a' }));
    await act(async () => queue.sent(99));
    expect(queue.ask).toEqual({ id: 1, text: 'a' });
    await act(async () => queue.sent(1));
    // Sent twice (a panel mounted again) is harmless.
    await act(async () => queue.sent(1));
    await act(async () => queue.enqueue({ text: 'b' }));
    expect(queue.ask).toEqual({ id: 2, text: 'b' });
  });
});
