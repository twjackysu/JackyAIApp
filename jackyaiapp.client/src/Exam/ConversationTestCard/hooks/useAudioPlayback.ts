import { useState, useCallback, useRef } from 'react';

export function useAudioPlayback() {
  const [playingMessageId, setPlayingMessageId] = useState<string | null>(null);
  const currentAudioRef = useRef<HTMLAudioElement | null>(null);

  const playMessage = useCallback(async (message: string, messageId?: string) => {
    try {
      // Stop any currently playing audio
      if (currentAudioRef.current) {
        currentAudioRef.current.pause();
        currentAudioRef.current = null;
      }

      setPlayingMessageId(messageId || null);
      
      // Create audio element to track when it ends
      const url = `/api/Audio/normal?text=${encodeURIComponent(message)}`;
      const audio = new Audio(url);
      currentAudioRef.current = audio;
      
      audio.onended = () => {
        setPlayingMessageId(null);
        currentAudioRef.current = null;
      };
      
      audio.onerror = () => {
        setPlayingMessageId(null);
        currentAudioRef.current = null;
        console.error('Error playing audio');
      };
      
      await audio.play();
    } catch (error) {
      setPlayingMessageId(null);
      currentAudioRef.current = null;
      console.error('Error playing audio:', error);
    }
  }, []);

  const stopPlaying = useCallback(() => {
    if (currentAudioRef.current) {
      currentAudioRef.current.pause();
      currentAudioRef.current = null;
    }
    setPlayingMessageId(null);
  }, []);

  return {
    playingMessageId,
    playMessage,
    stopPlaying,
  };
}