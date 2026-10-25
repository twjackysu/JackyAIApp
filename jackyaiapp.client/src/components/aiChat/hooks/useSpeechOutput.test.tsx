import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { synthesisOf, useSpeechOutput } from './useSpeechOutput';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

type SpeechOutput = ReturnType<typeof useSpeechOutput>;

/** Renders the hook for `text`; `result.current` is what it returned last. */
function renderHook(text: string) {
  const result = {} as { current: SpeechOutput };
  function Probe() {
    result.current = useSpeechOutput(text);
    return null;
  }
  const root: Root = createRoot(document.createElement('div'));
  act(() => root.render(<Probe />));
  return { result, unmount: () => act(() => root.unmount()) };
}

class FakeUtterance {
  lang = '';
  onend: (() => void) | null = null;
  onerror: (() => void) | null = null;
  constructor(public text: string) {}
}

describe('useSpeechOutput', () => {
  let spoken: FakeUtterance[];
  const synthesis = { speak: vi.fn((u: FakeUtterance) => spoken.push(u)), cancel: vi.fn() };

  beforeEach(() => {
    spoken = [];
    synthesis.speak.mockClear();
    synthesis.cancel.mockClear();
    vi.stubGlobal('speechSynthesis', synthesis);
    vi.stubGlobal('SpeechSynthesisUtterance', FakeUtterance);
  });
  afterEach(() => vi.unstubAllGlobals());

  it('is not offered where the browser cannot speak', () => {
    expect(synthesisOf({})).toBeNull();
    expect(
      synthesisOf({ speechSynthesis: synthesis, SpeechSynthesisUtterance: FakeUtterance }),
    ).not.toBeNull();
  });

  it('reads each language in its own voice, and is done after the last', () => {
    const { result } = renderHook('It was **serendipity**. 意外的幸運。');
    expect(result.current.supported).toBe(true);

    act(() => result.current.toggle());

    expect(result.current.speaking).toBe(true);
    expect(spoken.map((u) => [u.text, u.lang])).toEqual([
      ['It was serendipity.', 'en-US'],
      ['意外的幸運。', 'zh-TW'],
    ]);
    act(() => spoken[1].onend!());
    expect(result.current.speaking).toBe(false);
  });

  it('stops when pressed again, and when it leaves the screen', () => {
    const { result, unmount } = renderHook('hello');
    act(() => result.current.toggle());
    synthesis.cancel.mockClear();

    act(() => result.current.toggle());
    expect(result.current.speaking).toBe(false);
    expect(synthesis.cancel).toHaveBeenCalledTimes(1);

    act(() => result.current.toggle());
    synthesis.cancel.mockClear();
    unmount();
    expect(synthesis.cancel).toHaveBeenCalledTimes(1);
  });

  it('stops what was being read before, and a read cut short is not still speaking', () => {
    const { result } = renderHook('hello');
    act(() => result.current.toggle());
    expect(synthesis.cancel).toHaveBeenCalledTimes(1);

    act(() => spoken[0].onerror!());
    expect(result.current.speaking).toBe(false);
  });

  it('reads nothing of an answer with no words', () => {
    const { result } = renderHook('📈');
    act(() => result.current.toggle());
    expect(synthesis.speak).not.toHaveBeenCalled();
    expect(result.current.speaking).toBe(false);
  });
});
