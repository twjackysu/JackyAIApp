import { useEffect, useRef, useState } from 'react';

/**
 * Plays short exam audio clips one at a time and remembers which clips have already been
 * played, so a card can enforce the real TOEIC rule that each clip is heard once (the
 * Listening cards only re-enable replay after the question has been answered).
 *
 * Clips are addressed by an opaque key — an option id, or a sentinel such as '__prompt__'
 * for a question prompt — so a card can ask both "is this clip playing?" and "has this clip
 * been played?" without owning any audio state of its own. Starting a clip stops whatever
 * was playing before, and playback stops if the card unmounts mid-clip.
 */
export function useOneShotAudio() {
  const [playingKey, setPlayingKey] = useState<string | null>(null);
  const [playedKeys, setPlayedKeys] = useState<Set<string>>(new Set());
  const audioRef = useRef<HTMLAudioElement | null>(null);

  useEffect(() => {
    return () => {
      audioRef.current?.pause();
    };
  }, []);

  const play = (key: string, url: string) => {
    if (audioRef.current) {
      audioRef.current.pause();
      audioRef.current = null;
    }
    setPlayingKey(key);
    setPlayedKeys((prev) => new Set(prev).add(key));
    const audio = new Audio(url);
    audioRef.current = audio;
    audio.onended = () => setPlayingKey(null);
    audio.onerror = () => setPlayingKey(null);
    audio.play().catch(() => setPlayingKey(null));
  };

  /** Clears the played-once history. Call when moving on to a new question. */
  const resetPlayed = () => setPlayedKeys(new Set());

  return { playingKey, playedKeys, play, resetPlayed };
}
