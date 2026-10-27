import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import controllerCs from '../../../JackyAIApp.Server/Controllers/ConversationPracticeController.cs?raw';

import {
  MAX_RECORDING_SECONDS,
  RECORDING_BITS_PER_SECOND,
  START_RECORDING_LABEL,
  useVoiceRecording,
} from './useVoiceRecording';

const transcribe = vi.fn();

vi.mock('@/apis/examApis', () => ({
  useTranscribeAudioMutation: () => [transcribe, { isLoading: false }],
}));

/** A stand-in for the browser's recorder: start/stop only flip its state and fire its events. */
class FakeRecorder {
  static last: FakeRecorder | null = null;
  state: 'inactive' | 'recording' = 'inactive';
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  stopCalls = 0;
  constructor(
    readonly stream: unknown,
    readonly options?: MediaRecorderOptions,
  ) {
    FakeRecorder.last = this;
  }
  start() {
    this.state = 'recording';
  }
  stop() {
    this.stopCalls += 1;
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['audio']) });
    this.onstop?.();
  }
}

describe('useVoiceRecording', () => {
  beforeEach(() => {
    FakeRecorder.last = null;
    transcribe.mockReset();
    transcribe.mockReturnValue({ unwrap: () => Promise.resolve({ data: { text: 'hello' } }) });
    vi.stubGlobal('MediaRecorder', FakeRecorder);
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: () => Promise.resolve({ getTracks: () => [] }) },
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  /** Renders the hook and waits (on real timers) for the microphone to be ready. */
  async function ready(onTranscriptionComplete = vi.fn()) {
    const result = { current: null as unknown as ReturnType<typeof useVoiceRecording> };
    function Probe() {
      result.current = useVoiceRecording({ onTranscriptionComplete });
      return null;
    }
    const root = createRoot(document.createElement('div'));
    await act(async () => {
      root.render(createElement(Probe));
    });
    // getUserMedia resolves on a microtask; let it and the state update land.
    await act(async () => {
      await Promise.resolve();
    });
    expect(result.current.mediaRecorder).not.toBeNull();
    const hook = { result, unmount: () => act(() => root.unmount()) };
    return { hook, recorder: FakeRecorder.last!, onTranscriptionComplete };
  }

  it('asks the recorder for a speech bitrate', async () => {
    const { recorder } = await ready();
    expect(recorder.options?.audioBitsPerSecond).toBe(RECORDING_BITS_PER_SECOND);
  });

  it('stops by itself after the longest recording and transcribes what was said', async () => {
    const { hook, recorder, onTranscriptionComplete } = await ready();
    vi.useFakeTimers();

    act(() => hook.result.current.toggleRecording());
    expect(hook.result.current.isRecording).toBe(true);

    act(() => vi.advanceTimersByTime(MAX_RECORDING_SECONDS * 1000 - 1));
    expect(recorder.stopCalls).toBe(0);

    await act(async () => {
      vi.advanceTimersByTime(1);
    });
    expect(recorder.stopCalls).toBe(1);
    expect(hook.result.current.isRecording).toBe(false);
    expect(transcribe).toHaveBeenCalledTimes(1);

    vi.useRealTimers();
    await act(async () => {
      await Promise.resolve();
    });
    expect(onTranscriptionComplete).toHaveBeenCalledWith('hello', undefined);
  });

  it('does not stop a later recording with the timer of one the reader stopped', async () => {
    const { hook, recorder } = await ready();
    vi.useFakeTimers();

    act(() => hook.result.current.toggleRecording());
    await act(async () => {
      vi.advanceTimersByTime(60_000);
    });
    act(() => hook.result.current.toggleRecording()); // the reader stops it
    expect(recorder.stopCalls).toBe(1);

    act(() => hook.result.current.toggleRecording()); // and starts another
    // Past the moment the first recording's timer would have fired, short of the second's.
    await act(async () => {
      vi.advanceTimersByTime(60_001);
    });
    expect(recorder.stopCalls).toBe(1);
    expect(hook.result.current.isRecording).toBe(true);

    await act(async () => {
      vi.advanceTimersByTime(MAX_RECORDING_SECONDS * 1000 - 60_001);
    });
    expect(recorder.stopCalls).toBe(2);
  });

  it('drops the timer when the page goes away', async () => {
    const { hook, recorder } = await ready();
    vi.useFakeTimers();

    act(() => hook.result.current.toggleRecording());
    expect(vi.getTimerCount()).toBe(1);
    hook.unmount();
    expect(vi.getTimerCount()).toBe(0);
    const stopsAtUnmount = recorder.stopCalls;

    vi.advanceTimersByTime(MAX_RECORDING_SECONDS * 1000);
    expect(recorder.stopCalls).toBe(stopsAtUnmount);
    expect(transcribe).not.toHaveBeenCalled();
  });

  it('says the limit on the microphone button', () => {
    expect(START_RECORDING_LABEL).toBe('點擊開始錄音（最長 2 分鐘）');
  });

  it('keeps two minutes well under the size the server takes', () => {
    const match = /MaxAudioBytes\s*=\s*(\d+)\s*\*\s*1024\s*\*\s*1024;/.exec(controllerCs);
    expect(match).not.toBeNull();
    const serverBytes = Number(match![1]) * 1024 * 1024;
    const twoMinutesBytes = (MAX_RECORDING_SECONDS * RECORDING_BITS_PER_SECOND) / 8;
    // Room for a browser recording at twice the bitrate it was asked for.
    expect(twoMinutesBytes * 2).toBeLessThan(serverBytes);
  });
});
