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

export function useSpeechOutput(text: string) {
  const [speaking, setSpeaking] = useState(false);
  const speakingRef = useRef(false);
  const browser = synthesisOf();

  const stop = useCallback(() => {
    if (!speakingRef.current) return;
    speakingRef.current = false;
    setSpeaking(false);
    synthesisOf()?.synthesis.cancel();
  }, []);

  const start = useCallback(() => {
    const found = synthesisOf();
    if (!found) return;
    const segments = speechSegmentsOf(text);
    if (segments.length === 0) return;
    // Whatever is being read (another answer) stops; its last utterance's end or error resets its button.
    found.synthesis.cancel();
    speakingRef.current = true;
    setSpeaking(true);
    segments.forEach((segment, i) => {
      const utterance = new found.Utterance(segment.text);
      utterance.lang = segment.lang;
      if (i === segments.length - 1) {
        const ended = () => {
          speakingRef.current = false;
          setSpeaking(false);
        };
        utterance.onend = ended;
        utterance.onerror = ended;
      }
      found.synthesis.speak(utterance);
    });
  }, [text]);

  // An answer that leaves the screen (another conversation, a closed drawer) stops being read.
  useEffect(() => stop, [stop]);

  return {
    /** Whether this browser can read aloud at all; no button where it cannot. */
    supported: browser !== null,
    speaking,
    toggle: speaking ? stop : start,
  };
}
