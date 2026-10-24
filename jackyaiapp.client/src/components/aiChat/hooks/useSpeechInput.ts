import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Speaking a message instead of typing it, with the browser's own speech recognition (the Web Speech
 * API: Chrome, Edge and Safari have it, Firefox does not, so there the button is not shown). Nothing goes
 * to this site's server or costs an AI call: the words land in the message box, and the reader still
 * presses send. The browser may send the audio to its own recognition service, as it says when it asks
 * for the microphone.
 */

/** The part of the browser's `SpeechRecognition` used here. */
export interface SpeechRecognitionLike {
  lang: string;
  interimResults: boolean;
  continuous: boolean;
  start(): void;
  stop(): void;
  abort(): void;
  onresult: ((event: SpeechResultEventLike) => void) | null;
  onerror: ((event: { error: string }) => void) | null;
  onend: (() => void) | null;
}

/** The part of a `SpeechRecognitionEvent` used here: every result of the session so far. */
export interface SpeechResultEventLike {
  results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }>;
}

type RecognitionCtor = new () => SpeechRecognitionLike;

/** The browser's speech recognition, or null where it has none. */
export function recognitionCtor(w: object = window): RecognitionCtor | null {
  const found = w as { SpeechRecognition?: unknown; webkitSpeechRecognition?: unknown };
  const ctor = found.SpeechRecognition ?? found.webkitSpeechRecognition;
  return typeof ctor === 'function' ? (ctor as RecognitionCtor) : null;
}

/** What was heard so far in one session: every result's best guess, in order. */
export function transcriptOf(event: SpeechResultEventLike): string {
  let text = '';
  for (let i = 0; i < event.results.length; i++) text += event.results[i][0]?.transcript ?? '';
  return text.trim();
}

/** The message with what was heard after what the reader had typed (a space between, unless CJK meets CJK). */
export function joinSpeech(typed: string, heard: string): string {
  if (!heard) return typed;
  if (!typed.trim()) return heard;
  const cjk = /[\u3000-\u9fff\uff00-\uffef]/;
  const glue = /\s$/.test(typed) || (cjk.test(typed.slice(-1)) && cjk.test(heard[0])) ? '' : ' ';
  return typed + glue + heard;
}

/** What a recognition error means to the reader; null for one that is not worth saying (they stopped). */
export function speechErrorMessage(error: string): string | null {
  switch (error) {
    case 'aborted':
      return null;
    case 'not-allowed':
    case 'service-not-allowed':
      return '無法使用麥克風：請在瀏覽器允許這個網站使用麥克風。';
    case 'no-speech':
      return '沒有聽到聲音，請再試一次。';
    case 'audio-capture':
      return '找不到麥克風。';
    case 'network':
      return '語音辨識需要網路連線。';
    default:
      return '語音輸入失敗，請再試一次。';
  }
}

export interface SpeechInput {
  /** The browser can listen at all. */
  supported: boolean;
  listening: boolean;
  /** Why the last session failed, in words; cleared when a new one starts. */
  error: string | null;
  /** Starts listening, or stops (keeping what was heard). */
  toggle: () => void;
  /** Stops at once, dropping anything still to come (the message was sent). */
  cancel: () => void;
}

/**
 * Listens in Traditional Chinese (it also takes English words) until the reader pauses, giving what it
 * heard so far as it goes (`onHeard`, interim results included, so the words appear while they speak).
 */
export function useSpeechInput(onHeard: (transcript: string) => void, lang = 'zh-TW'): SpeechInput {
  const [supported] = useState(() => recognitionCtor() !== null);
  const [listening, setListening] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const recognition = useRef<SpeechRecognitionLike | null>(null);
  const heard = useRef(onHeard);
  useEffect(() => {
    heard.current = onHeard;
  }, [onHeard]);

  // Leaving the page (or the panel) stops the microphone.
  useEffect(() => () => recognition.current?.abort(), []);

  const toggle = useCallback(() => {
    if (recognition.current) {
      recognition.current.stop();
      return;
    }
    const Ctor = recognitionCtor();
    if (!Ctor) return;
    const r = new Ctor();
    r.lang = lang;
    r.interimResults = true;
    r.continuous = false;
    r.onresult = (event) => heard.current(transcriptOf(event));
    r.onerror = (event) => setError(speechErrorMessage(event.error));
    r.onend = () => {
      recognition.current = null;
      setListening(false);
    };
    recognition.current = r;
    setError(null);
    setListening(true);
    try {
      r.start();
    } catch {
      recognition.current = null;
      setListening(false);
      setError(speechErrorMessage(''));
    }
  }, [lang]);

  const cancel = useCallback(() => recognition.current?.abort(), []);

  return { supported, listening, error, toggle, cancel };
}
