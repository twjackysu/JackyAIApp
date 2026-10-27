import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { playGoogleNormal, playGoogleSlow } from './audio';

/** A stand-in for the browser's audio element: `play` does what the test says, `error` fired by hand. */
class FakeAudio {
  static last: FakeAudio | null = null;
  static nextPlay: () => Promise<void> = () => Promise.resolve();
  onerror: (() => void) | null = null;
  constructor(readonly src: string) {
    FakeAudio.last = this;
  }
  play() {
    return FakeAudio.nextPlay();
  }
}

class FakeUtterance {
  lang = '';
  rate = 1;
  constructor(readonly text: string) {}
}

const speak = vi.fn();
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('the site voice', () => {
  beforeEach(() => {
    FakeAudio.last = null;
    FakeAudio.nextPlay = () => Promise.resolve();
    speak.mockReset();
    vi.stubGlobal('Audio', FakeAudio);
    vi.stubGlobal('SpeechSynthesisUtterance', FakeUtterance);
    vi.stubGlobal('speechSynthesis', { cancel: vi.fn(), speak });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('plays the server clip and leaves the browser voice alone when it works', async () => {
    playGoogleNormal('Hello there.');
    await flush();
    expect(FakeAudio.last?.src).toBe('/api/Audio/normal?text=Hello%20there.');
    expect(speak).not.toHaveBeenCalled();
  });

  it('says it in the browser voice, once, when the server clip cannot be fetched', async () => {
    FakeAudio.nextPlay = () => Promise.reject(new DOMException('no source', 'NotSupportedError'));
    playGoogleSlow('Take it slow.');
    FakeAudio.last?.onerror?.();
    await flush();
    expect(speak).toHaveBeenCalledTimes(1);
    const utterance = speak.mock.calls[0][0] as FakeUtterance;
    expect(utterance.text).toBe('Take it slow.');
    expect(utterance.lang).toBe('en-US');
    expect(utterance.rate).toBeLessThan(1);
  });

  it('stays quiet when the browser holds the clip back or it was cut by the next one', async () => {
    FakeAudio.nextPlay = () => Promise.reject(new DOMException('autoplay', 'NotAllowedError'));
    playGoogleNormal('Hello.');
    FakeAudio.nextPlay = () => Promise.reject(new DOMException('paused', 'AbortError'));
    playGoogleNormal('Hello again.');
    await flush();
    expect(speak).not.toHaveBeenCalled();
  });
});
