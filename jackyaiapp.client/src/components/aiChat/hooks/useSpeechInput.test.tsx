import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  joinSpeech,
  recognitionCtor,
  speechErrorMessage,
  transcriptOf,
  useSpeechInput,
  type SpeechInput,
  type SpeechRecognitionLike,
  type SpeechResultEventLike,
} from './useSpeechInput';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const results = (...parts: [string, boolean][]): SpeechResultEventLike => ({
  results: parts.map(([transcript, isFinal]) => Object.assign([{ transcript }], { isFinal })),
});

describe('transcriptOf', () => {
  it('joins every result of the session, interim ones included', () => {
    expect(transcriptOf(results(['給我', true], ['今天的簡報', false]))).toBe('給我今天的簡報');
  });
});

describe('joinSpeech', () => {
  it('is what was heard when nothing was typed', () => {
    expect(joinSpeech('', '給我今天的簡報')).toBe('給我今天的簡報');
    expect(joinSpeech('  ', 'hello')).toBe('hello');
  });

  it('follows what was typed: a space between words, none between Chinese', () => {
    expect(joinSpeech('look up', 'serendipity')).toBe('look up serendipity');
    expect(joinSpeech('幫我查', '單字')).toBe('幫我查單字');
    expect(joinSpeech('查 ', 'apple')).toBe('查 apple');
  });

  it('keeps what was typed while nothing is heard yet', () => {
    expect(joinSpeech('幫我查', '')).toBe('幫我查');
  });
});

describe('speechErrorMessage', () => {
  it('says nothing when the reader stopped it, and why otherwise', () => {
    expect(speechErrorMessage('aborted')).toBeNull();
    expect(speechErrorMessage('not-allowed')).toContain('麥克風');
    expect(speechErrorMessage('no-speech')).toContain('沒有聽到');
    expect(speechErrorMessage('whatever')).toContain('失敗');
  });
});

describe('recognitionCtor', () => {
  it('finds the standard or the webkit one, or none', () => {
    class R {}
    expect(recognitionCtor({ SpeechRecognition: R })).toBe(R);
    expect(recognitionCtor({ webkitSpeechRecognition: R })).toBe(R);
    expect(recognitionCtor({})).toBeNull();
  });
});

// ── the hook, with a recognition the test drives ───────────────────────────────────────────

class FakeRecognition implements SpeechRecognitionLike {
  static last: FakeRecognition | null = null;
  lang = '';
  interimResults = false;
  continuous = true;
  onresult: SpeechRecognitionLike['onresult'] = null;
  onerror: SpeechRecognitionLike['onerror'] = null;
  onend: SpeechRecognitionLike['onend'] = null;
  started = false;
  aborted = false;
  constructor() {
    FakeRecognition.last = this;
  }
  start() {
    this.started = true;
  }
  stop() {
    this.onend?.();
  }
  abort() {
    this.aborted = true;
    this.onend?.();
  }
}

let container: HTMLDivElement;
let root: Root;
const probe = {} as { speech: SpeechInput };
const heard = vi.fn();

function Probe() {
  probe.speech = useSpeechInput(heard);
  return null;
}

beforeEach(() => {
  FakeRecognition.last = null;
  heard.mockClear();
  vi.stubGlobal('webkitSpeechRecognition', FakeRecognition);
  container = document.createElement('div');
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  vi.unstubAllGlobals();
});

describe('useSpeechInput', () => {
  it('listens in Traditional Chinese, giving what it hears as it goes, until the reader pauses', () => {
    act(() => root.render(<Probe />));
    expect(probe.speech.supported).toBe(true);

    act(() => probe.speech.toggle());
    const r = FakeRecognition.last!;
    expect(r).toMatchObject({
      started: true,
      lang: 'zh-TW',
      interimResults: true,
      continuous: false,
    });
    expect(probe.speech.listening).toBe(true);

    act(() => r.onresult!(results(['給我', false])));
    act(() => r.onresult!(results(['給我今天的簡報', true])));
    expect(heard.mock.calls.map((c) => c[0])).toEqual(['給我', '給我今天的簡報']);

    act(() => r.onend!());
    expect(probe.speech.listening).toBe(false);
  });

  it('stops when pressed again', () => {
    act(() => root.render(<Probe />));
    act(() => probe.speech.toggle());
    act(() => probe.speech.toggle());
    expect(probe.speech.listening).toBe(false);
  });

  it('says why it failed, and stops the microphone when it goes away', () => {
    act(() => root.render(<Probe />));
    act(() => probe.speech.toggle());
    const r = FakeRecognition.last!;
    act(() => r.onerror!({ error: 'not-allowed' }));
    act(() => r.onend!());
    expect(probe.speech.error).toContain('麥克風');

    act(() => probe.speech.toggle());
    expect(probe.speech.error).toBeNull();
    act(() => root.unmount());
    expect(FakeRecognition.last!.aborted).toBe(true);
    root = createRoot(container);
  });

  it('is not offered where the browser cannot listen', () => {
    vi.unstubAllGlobals();
    act(() => root.render(<Probe />));
    expect(probe.speech.supported).toBe(false);
  });
});
