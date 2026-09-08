import { useEffect, useState } from 'react';

import { useTranscribeAudioMutation } from '@/apis/examApis';

interface UseVoiceRecordingProps {
  onTranscriptionComplete: (text: string) => void;
}

/**
 * Microphone capture + Whisper transcription for the Translation and Conversation test cards.
 *
 * The microphone is opened once on mount (so the mic button can be enabled as soon as the
 * device is ready) and is held open between recordings, but it is released on unmount — the
 * returned cleanup stops every track, otherwise the browser keeps showing its "in use"
 * indicator and holds the device for the rest of the session after leaving the exam screen.
 */
export function useVoiceRecording({ onTranscriptionComplete }: UseVoiceRecordingProps) {
  const [isRecording, setIsRecording] = useState(false);
  const [mediaRecorder, setMediaRecorder] = useState<MediaRecorder | null>(null);
  const [transcribeAudio, { isLoading: isTranscribing }] = useTranscribeAudioMutation();

  useEffect(() => {
    // Tracked outside React state so the cleanup below can always reach them, including when
    // the component unmounts while getUserMedia is still pending.
    let cancelled = false;
    let stream: MediaStream | null = null;
    let recorder: MediaRecorder | null = null;

    const releaseMicrophone = () => {
      stream?.getTracks().forEach((track) => track.stop());
      stream = null;
    };

    const setupMediaRecorder = async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        // Unmounted while the permission prompt / device open was still in flight: the stream
        // only exists now, so this is the first (and only) chance to hand the mic back.
        if (cancelled) {
          releaseMicrophone();
          return;
        }

        recorder = new MediaRecorder(stream);

        const chunks: Blob[] = [];

        recorder.ondataavailable = (event) => {
          if (event.data.size > 0) {
            chunks.push(event.data);
          }
        };

        recorder.onstop = async () => {
          if (chunks.length > 0) {
            const audioBlob = new Blob(chunks, { type: 'audio/wav' });
            await handleAudioTranscription(audioBlob);
            chunks.length = 0;
          }
        };

        setMediaRecorder(recorder);
      } catch (error) {
        console.error('Error accessing microphone:', error);
      }
    };

    setupMediaRecorder();

    return () => {
      cancelled = true;

      if (recorder) {
        // Drop the handlers first: a recording abandoned by navigating away should not fire a
        // transcription request for a screen that no longer exists.
        recorder.ondataavailable = null;
        recorder.onstop = null;
        if (recorder.state !== 'inactive') {
          recorder.stop();
        }
        recorder = null;
      }

      releaseMicrophone();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleAudioTranscription = async (audioBlob: Blob) => {
    try {
      const formData = new FormData();
      formData.append('audioFile', audioBlob, 'recording.wav');

      const response = await transcribeAudio(formData).unwrap();
      if (response.data.text) {
        onTranscriptionComplete(response.data.text);
      }
    } catch (error) {
      console.error('Error transcribing audio:', error);
    }
  };

  const toggleRecording = () => {
    if (!mediaRecorder) return;

    if (isRecording) {
      mediaRecorder.stop();
      setIsRecording(false);
    } else {
      if (mediaRecorder.state === 'inactive') {
        mediaRecorder.start();
        setIsRecording(true);
      }
    }
  };

  return {
    isRecording,
    isTranscribing,
    mediaRecorder,
    toggleRecording,
  };
}
