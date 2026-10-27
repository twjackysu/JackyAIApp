import { useState, useCallback, useRef } from 'react';

/** Said under an AI message whose voice could not be played. */
export const PLAYBACK_FAILED_NOTE = '語音沒有播出來，請稍後再按一次播放。';

interface PlayOptions {
  /**
   * Played by itself as the message arrived, not by a press. A browser may refuse that until the reader
   * has touched the page (its autoplay rule); that is no failure, the play button is right there.
   */
  auto?: boolean;
}

/** Whether a refused `play()` is worth telling the reader about. */
export function playRefusalIsFailure(error: unknown, auto: boolean): boolean {
  const name = error instanceof DOMException || error instanceof Error ? error.name : '';
  // A play cut short by the next one (pause, or a new source) is the reader's own doing.
  if (name === 'AbortError') return false;
  if (name === 'NotAllowedError' && auto) return false;
  return true;
}

export function useAudioPlayback() {
  const [playingMessageId, setPlayingMessageId] = useState<string | null>(null);
  /** The message whose voice last failed to play; cleared by the next play or stop. */
  const [failedMessageId, setFailedMessageId] = useState<string | null>(null);
  const currentAudioRef = useRef<HTMLAudioElement | null>(null);

  const playMessage = useCallback(
    async (message: string, messageId?: string, { auto = false }: PlayOptions = {}) => {
      // Stop any currently playing audio
      if (currentAudioRef.current) {
        currentAudioRef.current.pause();
        currentAudioRef.current = null;
      }

      setPlayingMessageId(messageId || null);
      setFailedMessageId(null);

      const url = `/api/Audio/normal?text=${encodeURIComponent(message)}`;
      const audio = new Audio(url);
      currentAudioRef.current = audio;

      // Only the audio still current may change what is shown: a stopped one's late events are stale.
      const settle = (failed: boolean) => {
        if (currentAudioRef.current !== audio) return;
        currentAudioRef.current = null;
        setPlayingMessageId(null);
        if (failed) setFailedMessageId(messageId || null);
      };

      audio.onended = () => settle(false);
      // The voice could not be fetched or decoded (the server refused it, the usage budget ran out, offline).
      audio.onerror = () => settle(true);

      try {
        await audio.play();
      } catch (error) {
        settle(playRefusalIsFailure(error, auto));
      }
    },
    [],
  );

  const stopPlaying = useCallback(() => {
    if (currentAudioRef.current) {
      currentAudioRef.current.pause();
      currentAudioRef.current = null;
    }
    setPlayingMessageId(null);
    setFailedMessageId(null);
  }, []);

  return {
    playingMessageId,
    failedMessageId,
    playMessage,
    stopPlaying,
  };
}
