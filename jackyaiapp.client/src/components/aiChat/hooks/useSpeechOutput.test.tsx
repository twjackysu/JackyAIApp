import { StrictMode, act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { synthesisOf, useSpeechOutput } from './useSpeechOutput';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

type SpeechOutput = ReturnType<typeof useSpeechOutput>;

/** Renders the hook for `text`; `result.current` is what it returned last. */
function renderHook(text: string, autoReadKey?: string, strict = false) {
  const result = {} as { current: SpeechOutput };
  function Probe() {
    result.current = useSpeechOutput(text, autoReadKey);
    return null;
  }
  const root: Root = createRoot(document.createElement('div'));
  act(() =>
    root.render(
      strict ? (
        <StrictMode>
          <Probe />
        </StrictMode>
      ) : (
        <Probe />
      ),
    ),
  );
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

  it('a reading started elsewhere resets this one, and this one no longer stops it', () => {
    const a = renderHook('first answer');
    const b = renderHook('second answer');
    act(() => a.result.current.toggle());
    act(() => b.result.current.toggle());

    expect(a.result.current.speaking).toBe(false);
    expect(b.result.current.speaking).toBe(true);
    synthesis.cancel.mockClear();
    a.unmount();
    expect(synthesis.cancel).not.toHaveBeenCalled();
    expect(b.result.current.speaking).toBe(true);
    b.unmount();
  });

  it('a late event of an old reading does not end a new one', () => {
    const { result, unmount } = renderHook('hello');
    act(() => result.current.toggle());
    const old = spoken[0];
    act(() => result.current.toggle());
    act(() => result.current.toggle());

    act(() => old.onerror!());
    expect(result.current.speaking).toBe(true);
    unmount();
  });

  describe('read by itself (autoReadKey, D-120)', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('starts by itself once, and not again when drawn again', () => {
      const first = renderHook('Good morning.', 'answer-auto-1');
      act(() => vi.runAllTimers());
      expect(first.result.current.speaking).toBe(true);
      expect(spoken.map((u) => u.text)).toEqual(['Good morning.']);
      first.unmount();

      const again = renderHook('Good morning.', 'answer-auto-1');
      act(() => vi.runAllTimers());
      expect(again.result.current.speaking).toBe(false);
      expect(spoken).toHaveLength(1);
      again.unmount();
    });

    it('reads once under StrictMode, which mounts twice', () => {
      const { result, unmount } = renderHook('Twice mounted.', 'answer-auto-strict', true);
      act(() => vi.runAllTimers());
      expect(result.current.speaking).toBe(true);
      expect(spoken.map((u) => u.text)).toEqual(['Twice mounted.']);
      unmount();
    });

    it('reads nothing without a key, or where the browser cannot speak', () => {
      const plain = renderHook('Not asked.');
      act(() => vi.runAllTimers());
      expect(spoken).toHaveLength(0);
      plain.unmount();

      vi.unstubAllGlobals();
      const mute = renderHook('No voice.', 'answer-auto-mute');
      act(() => vi.runAllTimers());
      expect(mute.result.current.supported).toBe(false);
      expect(synthesis.speak).not.toHaveBeenCalled();
      mute.unmount();
    });
  });
});
