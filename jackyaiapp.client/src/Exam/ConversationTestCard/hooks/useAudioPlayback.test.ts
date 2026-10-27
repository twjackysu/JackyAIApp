import { act, createElement } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { playRefusalIsFailure, useAudioPlayback } from './useAudioPlayback';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** A stand-in for the browser's audio element: `play` does what the test says, events fired by hand. */
class FakeAudio {
  static all: FakeAudio[] = [];
  static nextPlay: () => Promise<void> = () => Promise.resolve();
  onended: (() => void) | null = null;
  onerror: (() => void) | null = null;
  paused = false;
  constructor(readonly src: string) {
    FakeAudio.all.push(this);
  }
  play() {
    return FakeAudio.nextPlay();
  }
  pause() {
    this.paused = true;
  }
}

describe('useAudioPlayback', () => {
  let root: Root;
  const hook = { current: null as unknown as ReturnType<typeof useAudioPlayback> };

  beforeEach(() => {
    FakeAudio.all = [];
    FakeAudio.nextPlay = () => Promise.resolve();
    vi.stubGlobal('Audio', FakeAudio);
    function Probe() {
      hook.current = useAudioPlayback();
      return null;
    }
    root = createRoot(document.createElement('div'));
    act(() => root.render(createElement(Probe)));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('marks the message whose voice could not be fetched', async () => {
    await act(() => hook.current.playMessage('Hello', 'ai-0'));
    expect(hook.current.playingMessageId).toBe('ai-0');

    act(() => FakeAudio.all[0].onerror?.());
    expect(hook.current.playingMessageId).toBeNull();
    expect(hook.current.failedMessageId).toBe('ai-0');

    // Playing it again forgets the failure.
    await act(() => hook.current.playMessage('Hello', 'ai-0'));
    expect(hook.current.failedMessageId).toBeNull();
  });

  it('says nothing when the browser holds back a voice that played by itself', async () => {
    FakeAudio.nextPlay = () => Promise.reject(new DOMException('autoplay', 'NotAllowedError'));
    await act(() => hook.current.playMessage('Hello', 'ai-0', { auto: true }));
    expect(hook.current.playingMessageId).toBeNull();
    expect(hook.current.failedMessageId).toBeNull();
  });

  it('says so when a pressed play is refused', async () => {
    FakeAudio.nextPlay = () => Promise.reject(new DOMException('no source', 'NotSupportedError'));
    await act(() => hook.current.playMessage('Hello', 'ai-2'));
    expect(hook.current.failedMessageId).toBe('ai-2');
  });

  it('ignores a stopped voice failing late', async () => {
    await act(() => hook.current.playMessage('First', 'ai-0'));
    await act(() => hook.current.playMessage('Second', 'ai-2'));
    expect(FakeAudio.all[0].paused).toBe(true);

    act(() => FakeAudio.all[0].onerror?.());
    expect(hook.current.playingMessageId).toBe('ai-2');
    expect(hook.current.failedMessageId).toBeNull();
  });
});

describe('playRefusalIsFailure', () => {
  it('counts only what the reader would want to know', () => {
    expect(playRefusalIsFailure(new DOMException('', 'AbortError'), false)).toBe(false);
    expect(playRefusalIsFailure(new DOMException('', 'NotAllowedError'), true)).toBe(false);
    expect(playRefusalIsFailure(new DOMException('', 'NotAllowedError'), false)).toBe(true);
    expect(playRefusalIsFailure(new DOMException('', 'NotSupportedError'), true)).toBe(true);
    expect(playRefusalIsFailure('odd', false)).toBe(true);
  });
});
