import { useCallback, useEffect, useRef, useState } from 'react';

import { speechSegmentsOf } from '../utils/speechText';

/**
 * Reading an answer aloud with the browser's own speech synthesis (the Web Speech API, which every current
 * browser has): nothing goes to this site's server or costs usage, like speaking a message in
 * (`useSpeechInput`). Each run of English or Chinese (`speechSegmentsOf`) is read in a voice of its language,
 * so an English passage sounds English. One answer is read at a time: starting another stops this one.
 */

/** The part of the browser's `speechSynthesis` used here. */
export interface SynthesisLike {
  speak(utterance: SpeechSynthesisUtterance): void;
  cancel(): void;
}

type UtteranceCtor = new (text: string) => SpeechSynthesisUtterance;

/** The browser's speech synthesis, or null where it has none. */
export function synthesisOf(
  w: object = window,
): { synthesis: SynthesisLike; Utterance: UtteranceCtor } | null {
  const found = w as { speechSynthesis?: SynthesisLike; SpeechSynthesisUtterance?: unknown };
  return found.speechSynthesis && typeof found.SpeechSynthesisUtterance === 'function'
    ? {
        synthesis: found.speechSynthesis,
        Utterance: found.SpeechSynthesisUtterance as UtteranceCtor,
      }
    : null;
}

/**
 * The one reading under way, across every answer on the page: only its owner may cancel the browser's speech,
 * so an answer whose own reading was already cut off (some browsers send no event for a cancelled utterance)
 * never stops another's, and a late event of an old reading never resets a new one.
 */
let current: { token: symbol; reset: () => void } | null = null;

/**
 * The answers already read on their own (`autoReadKey`), by key: each is read once, so drawing it again (another
 * conversation and back, the drawer closed and opened) does not read it again.
 */
const autoRead = new Set<string>();

/**
 * Reads `text` aloud on the reader's press (`toggle`). With `autoReadKey` (an answer asked to be read aloud once done,
 * D-120) it also starts by itself, once per key in the page's life.
 */
export function useSpeechOutput(text: string, autoReadKey?: string) {
  const [speaking, setSpeaking] = useState(false);
  const token = useRef<symbol | null>(null);
  // The utterances being read: kept referenced, since a browser may drop an unreferenced one's end event.
  const utterances = useRef<SpeechSynthesisUtterance[]>([]);
  const browser = synthesisOf();

  const stop = useCallback(() => {
    setSpeaking(false);
    utterances.current = [];
    if (token.current && current?.token === token.current) {
      current = null;
      synthesisOf()?.synthesis.cancel();
    }
    token.current = null;
  }, []);

  const start = useCallback(() => {
    const found = synthesisOf();
    if (!found) return;
    const segments = speechSegmentsOf(text);
    if (segments.length === 0) return;
    // Whatever is being read (another answer) stops, and its button goes back to 朗讀.
    current?.reset();
    found.synthesis.cancel();
    const mine = Symbol('reading');
    const reset = () => {
      if (token.current !== mine) return;
      token.current = null;
      utterances.current = [];
      setSpeaking(false);
    };
    token.current = mine;
    current = { token: mine, reset };
    setSpeaking(true);
    utterances.current = segments.map((segment, i) => {
      const utterance = new found.Utterance(segment.text);
      utterance.lang = segment.lang;
      if (i === segments.length - 1) {
        const ended = () => {
          if (current?.token === mine) current = null;
          reset();
        };
        utterance.onend = ended;
        utterance.onerror = ended;
      }
      return utterance;
    });
    utterances.current.forEach((u) => found.synthesis.speak(u));
  }, [text]);

  // An answer that leaves the screen (another conversation, a closed drawer) stops being read.
  useEffect(() => stop, [stop]);

  // Started a moment later, and counted as read only then: a mount that is undone at once (React's StrictMode
  // mounts twice in development) reads nothing, and the one that stays reads it.
  useEffect(() => {
    if (!autoReadKey || autoRead.has(autoReadKey) || !synthesisOf()) return;
    const timer = setTimeout(() => {
      autoRead.add(autoReadKey);
      start();
    }, 0);
    return () => clearTimeout(timer);
  }, [autoReadKey, start]);

  return {
    /** Whether this browser can read aloud at all; no button where it cannot. */
    supported: browser !== null,
    speaking,
    toggle: speaking ? stop : start,
  };
}
