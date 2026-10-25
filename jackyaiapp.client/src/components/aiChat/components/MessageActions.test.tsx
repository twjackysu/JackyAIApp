import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';

import MessageActions from './MessageActions';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('MessageActions', () => {
  let root: Root | undefined;
  let container: HTMLDivElement;

  const render = (readAloud: boolean) => {
    container = document.createElement('div');
    root = createRoot(container);
    act(() =>
      root!.render(<MessageActions text="hello 你好" align="left" readAloud={readAloud} />),
    );
  };
  const button = (label: string) => container.querySelector(`button[aria-label="${label}"]`);

  afterEach(() => {
    act(() => root?.unmount());
    vi.unstubAllGlobals();
  });

  it('reads an answer aloud where the browser can speak, and stops it', () => {
    const speak = vi.fn();
    vi.stubGlobal('speechSynthesis', { speak, cancel: vi.fn() });
    vi.stubGlobal(
      'SpeechSynthesisUtterance',
      class {
        lang = '';
        constructor(public text: string) {}
      },
    );
    render(true);

    act(() => (button('朗讀') as HTMLButtonElement).click());

    expect(speak).toHaveBeenCalledTimes(2);
    expect(button('停止朗讀')).not.toBeNull();
  });

  it('has no read-aloud button where the browser cannot speak, nor on the reader’s own messages', () => {
    render(true);
    expect(button('朗讀')).toBeNull();
    expect(button('複製')).not.toBeNull();

    act(() => root?.unmount());
    vi.stubGlobal('speechSynthesis', { speak: vi.fn(), cancel: vi.fn() });
    vi.stubGlobal('SpeechSynthesisUtterance', class {});
    render(false);
    expect(button('朗讀')).toBeNull();
  });
});
